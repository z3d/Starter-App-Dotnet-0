namespace StarterApp.Tests.Conventions;

// docs/DECISIONS.md "The worker calls the application's handlers in its own process".
public partial class WorkerConventionTests
{
    private static readonly string WorkerSeam = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.Application", "Infrastructure", "Worker");
    private static readonly string Functions = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.Functions");

    // Every request the Functions worker may send. A function that should change or read something new adds its request here and its method to IWorkerOperations.
    internal static readonly HashSet<string> WorkerRequests = new(StringComparer.Ordinal)
    {
        "GetOrderByIdQuery",
        "GetCustomerQuery",
    };

    [Fact]
    public void TheWorkerSendsOnlyTheListedRequests()
    {
        var sent = SentRequests(SourceFiles(WorkerSeam).Select(File.ReadAllText));
        Assert.NotEmpty(sent);

        var failures = sent.Where(request => !WorkerRequests.Contains(request)).Select(request => $"The worker's operations send {request}, which is not in WorkerRequests.")
            .Concat(WorkerRequests.Where(request => !sent.Contains(request)).Select(request => $"{request} is in WorkerRequests but the worker's operations do not send it; take it off the list."))
            .ToList();

        Assert.True(failures.Count == 0, "The worker sends only the requests listed here:\n" + string.Join("\n", failures));
        Assert.Equal(typeof(IWorkerOperations).GetMethods().Length, typeof(UnconfiguredWorkerOperations).GetInterfaceMap(typeof(IWorkerOperations)).TargetMethods.Length);
    }

    [Fact]
    public void TheRequestList_CatchesARequestItDoesNotName()
    {
        var sent = SentRequests(["await mediator.SendAsync(new CancelOrderCommand { OrderId = id }, token);"]);

        Assert.Contains("CancelOrderCommand", sent);
        Assert.DoesNotContain("CancelOrderCommand", WorkerRequests);
    }

    [Fact]
    public void AFunction_NamesNothingOfTheApplicationButTheWorkersOperations()
    {
        var files = SourceFiles(Functions).ToList();
        Assert.NotEmpty(files);

        var failures = files.SelectMany(file => ApplicationNamesIn(File.ReadAllText(file)).Select(name => $"{Path.GetRelativePath(Functions, file)} names {name}")).ToList();

        Assert.True(failures.Count == 0,
            "A function names nothing of the application but StarterApp.Application.Infrastructure.Worker: no mediator, handler, context or domain type, so every change it causes goes through IWorkerOperations:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData("using StarterApp.Application.Infrastructure.Mediator;")]
    [InlineData("var order = new StarterApp.Domain.Entities.Order();")]
    [InlineData("global using StarterApp.Application.Data;")]
    public void TheFunctionRule_CatchesAnApplicationName(string line)
    {
        Assert.NotEmpty(ApplicationNamesIn(line));
    }

    [Fact]
    public void TheWorker_ReferencesTheApplicationProject_AndNeverTheWebHost()
    {
        var references = XDocument.Load(Path.Combine(Functions, "StarterApp.Functions.csproj"))
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .ToList();

        Assert.Contains("StarterApp.Application", references);
        Assert.DoesNotContain("StarterApp.Api", references);
        Assert.DoesNotContain(typeof(OrderConfirmationEmailFunction).Assembly.GetReferencedAssemblies(), name => name.Name == "StarterApp.Api");
    }

    // A grant the worker's login lacks fails a deployed message, not a test, unless each operation runs on that login here.
    [Fact]
    public void EveryOperation_RunsOnTheWorkersLogin_InItsIntegrationTests()
    {
        var source = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "StarterApp.Tests", "Integration", "WorkerOperationsTests.cs"));
        var operations = typeof(IWorkerOperations).GetMethods().Select(method => method.Name).ToList();
        Assert.NotEmpty(operations);

        Assert.Contains("_fixture.WorkerConnectionString", source, StringComparison.Ordinal);
        var missing = operations.Where(name => !source.Contains($".{name}(", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "WorkerOperationsTests runs no case of: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData(typeof(StarterApp.Application.Infrastructure.Validation.ValidationException), true)]
    [InlineData(typeof(EntityNotFoundException), true)]
    [InlineData(typeof(DomainRuleException), true)]
    [InlineData(typeof(ForbiddenAccessException), true)]
    [InlineData(typeof(StarterApp.Application.Infrastructure.Idempotency.IdempotencyKeyReusedException), true)]
    [InlineData(typeof(DbUpdateConcurrencyException), false)]
    [InlineData(typeof(TimeoutException), false)]
    public void ARefusalIsWhatARedeliveryCannotChange(Type exception, bool refused)
    {
        var instance = exception == typeof(StarterApp.Application.Infrastructure.Validation.ValidationException)
            ? new StarterApp.Application.Infrastructure.Validation.ValidationException([new ValidationError("Field", "is wrong")])
            : (Exception)Activator.CreateInstance(exception, "refused")!;

        Assert.Equal(refused, WorkerOperations.IsRefusal(instance));
    }

    [Fact]
    public async Task WithNoDatabase_EveryOperationIsRefused_SoItsMessageDeadLetters()
    {
        IWorkerOperations operations = new UnconfiguredWorkerOperations();

        var refusal = await Assert.ThrowsAsync<WorkerOperationRefusedException>(() => operations.GetOrderConfirmationAsync("tenant", "owner", Guid.CreateVersion7(), CancellationToken.None));

        Assert.True(MessageSettlement.IsNonRetryable(refusal));
    }

    [Fact]
    public void AHostThatIsNotTheApi_BuildsEveryRegistration()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development", ContentRootPath = FunctionsOutput });
        builder.Configuration.AddJsonFile(Path.Combine(FunctionsOutput, "appsettings.json"), optional: false);
        builder.Configuration["ConnectionStrings:database"] = "Host=localhost;Database=none;Username=none;Password=none";
        builder.AddWorkerOperations();

        using var services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<WorkerOperations>(services.GetRequiredService<IWorkerOperations>());
        Assert.NotEmpty(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeatureToggleOptions>>().Value.Entries);
    }

    [Fact]
    public void TheWorkersOutput_CarriesTheApplicationsSettings()
    {
        var settings = Path.Combine(FunctionsOutput, "appsettings.json");

        Assert.True(File.Exists(settings), $"{settings} is missing: link src/StarterApp.Api/appsettings.json into the worker with CopyToOutputDirectory and CopyToPublishDirectory.");
        Assert.Equal(File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.Api", "appsettings.json")), File.ReadAllText(settings));
    }

    // The tests run from tests/StarterApp.Tests/bin/<configuration>/<framework>; the worker is built beside it under the same two names.
    private static string FunctionsOutput
    {
        get
        {
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            return Path.Combine(Functions, "bin", framework.Parent!.Name, framework.Name);
        }
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    internal static HashSet<string> SentRequests(IEnumerable<string> sources) =>
        sources.SelectMany(source => SentRequest().Matches(source)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    internal static List<string> ApplicationNamesIn(string source) =>
        ApplicationName().Matches(source)
            .Select(match => match.Value)
            .Where(name => !name.StartsWith("StarterApp.Application.Infrastructure.Worker", StringComparison.Ordinal))
            .ToList();

    [GeneratedRegex(@"new (\w+(?:Command|Query))\b")]
    private static partial Regex SentRequest();

    // Anything of the solution but the worker itself and the service defaults it shares with every host.
    [GeneratedRegex(@"\bStarterApp\.(?!Functions\b|ServiceDefaults\b)\w+(\.\w+)*")]
    private static partial Regex ApplicationName();
}
