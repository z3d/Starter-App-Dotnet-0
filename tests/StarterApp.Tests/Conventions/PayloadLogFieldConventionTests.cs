namespace StarterApp.Tests.Conventions;

// Every field the code sends over HTTP or Service Bus has a recorded decision on whether its value may be logged.
public class PayloadLogFieldConventionTests : ConventionTestBase
{
    private static readonly string FieldsFile = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.ServiceDefaults", "Payloads", "payload-log-fields.txt");

    // The problem-details body every error carries; ProducesProblem metadata names no type to walk.
    private static readonly string[] ProblemFields = ["type", "title", "status", "detail", "instance", "traceId", "errors"];

    [Fact]
    public void EveryFieldTheCodeSends_HasALogDecision()
    {
        var (kept, masked) = StarterApp.ServiceDefaults.Payloads.PayloadLogFields.Parse(File.ReadAllText(FieldsFile));
        var decided = kept.Concat(masked).Select(Normalize).ToHashSet(StringComparer.Ordinal);

        var undecided = SentFieldNames()
            .Where(name => !decided.Contains(Normalize(name)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(undecided.Count == 0,
            "These fields reach the payload log with no decision. Add each to src/StarterApp.ServiceDefaults/Payloads/payload-log-fields.txt " +
            "as 'keep <field>' (its value may be logged) or 'mask <field>':\n" + string.Join("\n", undecided));
    }

    [Fact]
    public void TheFieldList_NamesOnlyFieldsTheCodeSends_EachOnce()
    {
        var (kept, masked) = StarterApp.ServiceDefaults.Payloads.PayloadLogFields.Parse(File.ReadAllText(FieldsFile));
        var listed = kept.Concat(masked).Select(Normalize).ToList();
        var sent = SentFieldNames().Select(Normalize).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(listed.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key));
        var stale = kept.Concat(masked).Where(name => !sent.Contains(Normalize(name))).ToList();
        Assert.True(stale.Count == 0, "Listed in payload-log-fields.txt but sent by nothing; remove them: " + string.Join(", ", stale));
    }

    [Fact]
    public void NoKeptField_IsSensitiveOrASecret()
    {
        var (kept, _) = StarterApp.ServiceDefaults.Payloads.PayloadLogFields.Parse(File.ReadAllText(FieldsFile));

        var wrong = kept
            .Where(name => StarterApp.ServiceDefaults.Payloads.PayloadSecretMasker.IsSecretName(name) ||
                StarterApp.ServiceDefaults.Payloads.PayloadCaptureOptions.DefaultSensitivePropertyNames.Any(sensitive => Normalize(name).Contains(Normalize(sensitive), StringComparison.Ordinal)))
            .ToList();

        Assert.True(wrong.Count == 0, "Kept fields that match a sensitive or secret name, and would be masked anyway: " + string.Join(", ", wrong));
    }

    [Fact]
    public void TheEmbeddedList_IsTheCheckedInFile()
    {
        var (kept, masked) = StarterApp.ServiceDefaults.Payloads.PayloadLogFields.Parse(File.ReadAllText(FieldsFile));

        Assert.Equal(kept, StarterApp.ServiceDefaults.Payloads.PayloadLogFields.Kept);
        Assert.Equal(masked, StarterApp.ServiceDefaults.Payloads.PayloadLogFields.Masked);
    }

    internal static IReadOnlyCollection<string> SentFieldNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<Type>();
        foreach (var type in EndpointPayloadTypes())
            Walk(type, names, visited);

        foreach (var snapshot in Directory.GetFiles(Path.Combine(TestPaths.RepoRoot, "tests", "StarterApp.Tests", "Contracts", "snapshots"), "*.json"))
            CollectKeys(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(snapshot)), names);

        names.UnionWith(ProblemFields);
        return names;
    }

    private static IEnumerable<Type> EndpointPayloadTypes()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddSingleton<IMediator, NoMediator>();
        builder.Services.AddSingleton(TimeProvider.System);
        var app = builder.Build();
        app.MapApiEndpoints();
        StarterApp.Application.Infrastructure.ClientEvents.ClientEventIntake.MapClientEventIntake(app);

        foreach (var endpoint in ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints))
        {
            foreach (var accepts in endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>())
                if (accepts.RequestType is { } requestType)
                    yield return requestType;
            foreach (var produces in endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>())
                if (produces.Type is { } responseType && responseType != typeof(void))
                    yield return responseType;
        }
    }

    private static void Walk(Type type, HashSet<string> names, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsLeaf(type) || !visited.Add(type))
            return;

        var dictionary = type.GetInterfaces().Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionary is not null)
        {
            Walk(dictionary.GetGenericArguments()[1], names, visited);
            return;
        }

        var enumerable = type.IsArray ? type.GetElementType() : type.GetInterfaces().Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
        if (enumerable is not null)
        {
            Walk(enumerable, names, visited);
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 ||
                property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is not null ||
                property.GetCustomAttribute<System.Text.Json.Serialization.JsonExtensionDataAttribute>() is not null)
                continue;

            names.Add(property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            Walk(property.PropertyType, names, visited);
        }
    }

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
        type == typeof(TimeSpan) || type == typeof(object) || typeof(IResult).IsAssignableFrom(type) || type == typeof(System.Collections.IEnumerable);

    private static void CollectKeys(System.Text.Json.Nodes.JsonNode? node, HashSet<string> names)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject jsonObject:
                foreach (var (key, value) in jsonObject)
                {
                    names.Add(key);
                    CollectKeys(value, names);
                }
                break;
            case System.Text.Json.Nodes.JsonArray jsonArray:
                foreach (var item in jsonArray)
                    CollectKeys(item, names);
                break;
        }
    }

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed class NoMediator : IMediator
    {
        public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
