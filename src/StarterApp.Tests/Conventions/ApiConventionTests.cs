using StarterApp.Api.Infrastructure.Mediator;
using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Tests.Conventions;

public class ApiConventionTests : ConventionTestBase
{
    // === Endpoint Definitions ===

    [Fact]
    public void EndpointDefinitions_MustNotDependOnDbContext()
    {
        var endpointTypes = ApiAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.Name == "IEndpointDefinition") &&
                   t.IsClass && !t.IsAbstract);

        endpointTypes
            .MustConformTo(Convention.MustNotTakeADependencyOn(
                typeof(StarterApp.Api.Data.ApplicationDbContext),
                "Endpoints must dispatch through the mediator, not access DbContext directly"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void EndpointDefinitions_MustNotDependOnIDbConnection()
    {
        var endpointTypes = ApiAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.Name == "IEndpointDefinition") &&
                   t.IsClass && !t.IsAbstract);

        endpointTypes
            .MustConformTo(Convention.MustNotTakeADependencyOn(
                typeof(System.Data.IDbConnection),
                "Endpoints must dispatch through the mediator, not access IDbConnection directly"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void ApiEndpointGroups_MustRequireGatewayIdentity()
    {
        var endpointFiles = Directory.EnumerateFiles(Path.Combine(FindRepoRoot(), "src", "StarterApp.Api", "Endpoints"), "*Endpoints.cs", SearchOption.TopDirectoryOnly);
        var failures = endpointFiles
            .SelectMany(FindUnprotectedApiRouteGroups)
            .ToList();

        Assert.True(failures.Count == 0,
            "API endpoint groups must opt into the trusted gateway identity middleware:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void GatewayIdentityHeaders_MustOnlyBeReadByIdentityInfrastructure()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "src", "StarterApp.Api");
        var identityRoot = Path.Combine(apiRoot, "Infrastructure", "Identity");
        var failures = Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(identityRoot, StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { File = file, Line = line, LineNumber = index + 1 })
                .Where(item => item.Line.Contains("X-Authenticated-", StringComparison.Ordinal) ||
                               item.Line.Contains("X-Gateway-Assertion", StringComparison.Ordinal))
                .Select(item => $"{FormatPath(item.File)}:{item.LineNumber} raw gateway identity headers must stay behind ICurrentUser/GatewayIdentity infrastructure."))
            .ToList();

        Assert.True(failures.Count == 0,
            "Production code must not read or define gateway identity headers outside the identity infrastructure:\n" + string.Join("\n", failures));
    }

    // === Validator Conventions ===

    [Fact]
    public void Validators_MustNotDependOnDbContext()
    {
        var validatorTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)));

        validatorTypes
            .MustConformTo(Convention.MustNotTakeADependencyOn(
                typeof(StarterApp.Api.Data.ApplicationDbContext),
                "Validators must be pure - no database access"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void Validators_MustNotDependOnIDbConnection()
    {
        var validatorTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)));

        validatorTypes
            .MustConformTo(Convention.MustNotTakeADependencyOn(
                typeof(System.Data.IDbConnection),
                "Validators must be pure - no database access"))
            .WithFailureAssertion(Assert.Fail);
    }

    // === DTO Conventions ===

    [Fact]
    public void DTOs_MustNotHaveBehavior()
    {
        var dtoTypes = GetApiContractTypes()
            .Where(t => t.Name.EndsWith("Dto") || t.Name.EndsWith("ReadModel"));

        dtoTypes
            .MustConformTo(new MustNotHaveInstanceMethodsConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void ApiContracts_MustHavePublicParameterlessConstructors()
    {
        var failures = GetApiContractTypes()
            .Where(t => t.GetConstructor(Type.EmptyTypes)?.IsPublic != true)
            .Select(t => $"{t.FullName} must expose a public parameterless constructor for JSON binding/serialization.")
            .ToList();

        Assert.True(failures.Count == 0,
            "API contracts must be simple serializable shapes:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ApiContracts_MustHavePublicSetters()
    {
        var failures = GetApiContractTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.SetMethod?.IsPublic != true)
                .Select(p => $"{t.FullName}.{p.Name} must have a public setter for JSON binding/serialization."))
            .ToList();

        Assert.True(failures.Count == 0,
            "API contract properties must be writable by serializers/model binding:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ApiContracts_MustUseMaterializedCollectionProperties()
    {
        var failures = GetApiContractTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => IsEnumerableButNotString(p.PropertyType))
                .Where(p => !IsMaterializedCollectionType(p.PropertyType))
                .Select(p => $"{t.FullName}.{p.Name} is {FormatTypeName(p.PropertyType)}. Use a materialized collection type such as List<T> so responses cannot expose lazy/deferred enumerables."))
            .ToList();

        Assert.True(failures.Count == 0,
            "API contract collection properties must be eager/materialized:\n" + string.Join("\n", failures));
    }

    // === Mapper Conventions ===

    [Fact]
    public void Mappers_ShouldBeStaticClasses()
    {
        var mapperTypes = ApiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Mapper") &&
                   t.IsClass && !IsCompilerGenerated(t));

        mapperTypes
            .MustConformTo(new MustBeStaticClassConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    // === Handler Conventions ===

    [Fact]
    public void Handlers_MustNotDependOnIMediator()
    {
        var handlerTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   (t.Name.EndsWith("CommandHandler") || t.Name.EndsWith("QueryHandler")) &&
                   !IsCompilerGenerated(t));

        handlerTypes
            .MustConformTo(Convention.MustNotTakeADependencyOn(
                typeof(IMediator),
                "Handlers must not dispatch to other handlers via IMediator - call services directly"))
            .WithFailureAssertion(Assert.Fail);
    }

    // === DateTimeOffset Enforcement ===

    [Fact]
    public void ApiTypes_MustUseDateTimeOffsetNotDateTime()
    {
        var apiTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t) &&
                   (t.Name.EndsWith("Dto") || t.Name.EndsWith("ReadModel") ||
                    t.Name.EndsWith("Command") || t.Name.EndsWith("Query") ||
                    t.Name == "OutboxMessage"));

        apiTypes
            .MustConformTo(new MustNotUseDateTimePropertiesConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    // === Domain Layer Isolation ===

    [Fact]
    public void DomainTypes_MustNotDependOnApiAssembly()
    {
        var domainTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));

        domainTypes
            .MustConformTo(new MustNotReferenceAssemblyConvention(ApiAssembly))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void DomainAssembly_MustNotReferenceThirdPartyAssemblies()
    {
        var allowedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System",
            "netstandard"
        };

        var failures = DomainAssembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .Where(name => !allowedNames.Contains(name))
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(failures.Count == 0,
            "Domain assembly must stay free of third-party and application-layer dependencies:\n" +
            string.Join("\n", failures));
    }

    // === Custom Convention Specifications ===

    private static IEnumerable<Type> GetApiContractTypes()
    {
        return ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t))
            .Where(t =>
                t.Name.EndsWith("Dto") ||
                t.Name.EndsWith("ReadModel") ||
                t.Name.StartsWith("PagedResponse", StringComparison.Ordinal));
    }

    private static bool IsEnumerableButNotString(Type type)
    {
        return type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    private static bool IsMaterializedCollectionType(Type type)
    {
        if (type.IsArray)
            return true;

        if (type.IsInterface || type.IsAbstract)
            return false;

        return typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    private static string FormatTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatTypeName))}>";
    }

    private static string FindRepoRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "StarterApp.slnx")) ||
                    File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string FormatPath(string file)
    {
        return Path.GetRelativePath(FindRepoRoot(), file);
    }

    private static IEnumerable<string> FindUnprotectedApiRouteGroups(string file)
    {
        var lines = File.ReadAllLines(file);
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains("MapGroup(\"/api/v1", StringComparison.Ordinal))
                continue;

            var statement = lines[index];
            var endIndex = index;
            while (!statement.Contains(';', StringComparison.Ordinal) && endIndex + 1 < lines.Length)
            {
                endIndex++;
                statement += "\n" + lines[endIndex];
            }

            if (!statement.Contains(".RequireGatewayIdentity()", StringComparison.Ordinal))
                yield return $"{FormatPath(file)}:{index + 1} /api/v1 MapGroup must call RequireGatewayIdentity() in the same route-group declaration.";
        }
    }

    private class MustNotUseDateTimePropertiesConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must use DateTimeOffset instead of DateTime for all timestamp properties";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var dateTimeProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?))
                .ToList();

            return dateTimeProperties.Count == 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} uses DateTime on: {string.Join(", ", dateTimeProperties.Select(p => p.Name))}. Use DateTimeOffset instead.");
        }
    }

    private class MustNotHaveInstanceMethodsConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must not have instance methods (DTOs should be plain data carriers)";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var instanceMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName) // exclude property getters/setters
                .Where(m => m.DeclaringType == type) // exclude inherited
                .ToList();

            return instanceMethods.Count == 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} has instance methods: {string.Join(", ", instanceMethods.Select(m => m.Name))}. DTOs should be plain data carriers.");
        }
    }

    private class MustBeStaticClassConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must be a static class";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            return type.IsAbstract && type.IsSealed // C# static classes are abstract + sealed
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} must be a static class (mappers should have no instance state)");
        }
    }

    private class MustNotReferenceAssemblyConvention : ConventionSpecification
    {
        private readonly Assembly _forbiddenAssembly;

        public MustNotReferenceAssemblyConvention(Assembly forbiddenAssembly)
        {
            _forbiddenAssembly = forbiddenAssembly;
        }

        protected override string FailureMessage => $"must not reference {_forbiddenAssembly.GetName().Name}";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var referencedAssemblies = type.Assembly.GetReferencedAssemblies();
            var references = referencedAssemblies.Any(a => a.FullName == _forbiddenAssembly.GetName().FullName);

            return !references
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name}'s assembly references {_forbiddenAssembly.GetName().Name} - domain layer must remain independent");
        }
    }
}
