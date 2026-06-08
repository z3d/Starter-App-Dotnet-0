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
    public void ProductionCode_MustNotConstructOwnedAggregatesWithoutOwnerScope()
    {
        // Order, Customer, and Product are owner-scoped resources. Their parameterless-ownership
        // constructors stamp OwnershipDefaults.Legacy* sentinels and exist only for EF/tests.
        // Production code must build them through the owner-aware constructor (the overload with an
        // `ownerSubject` parameter) using ICurrentUser-derived scope; otherwise a future handler
        // could silently create rows invisible to/uneditable by their real owner. CLAUDE.md owner-scoping.
        var ownedAggregates = new[] { "Order", "Customer", "Product" };

        // GetTypes() returns every type in the assembly — including compiler-generated async state
        // machines and lambda display classes (e.g. the EF retry delegate that constructs the Order),
        // so scanning each type's own declared methods reaches constructions however deeply nested.
        const BindingFlags methodFlags = BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var allTypes = ApiAssembly.GetTypes().Where(t => t.IsClass).ToList();
        Assert.NotEmpty(allTypes);

        var failures = new List<string>();
        var ownerAwareConstructions = 0;

        foreach (var type in allTypes)
        {
            foreach (var method in type.GetMethods(methodFlags))
            {
                foreach (var ctor in ResolveConstructedAggregates(method, ownedAggregates))
                {
                    var hasOwnerScope = ctor.GetParameters()
                        .Any(p => string.Equals(p.Name, "ownerSubject", StringComparison.Ordinal));

                    if (hasOwnerScope)
                        ownerAwareConstructions++;
                    else
                        failures.Add($"{OutermostType(type).FullName} constructs {ctor.DeclaringType!.Name} via a constructor " +
                                     "without owner scope (stamps legacy owner/tenant). Use the constructor that takes ownerSubject/tenantId.");
                }
            }
        }

        // Vacuous-pass guard: the three create handlers must surface as owner-aware constructions,
        // proving the newobj IL scan actually resolves these aggregates rather than matching nothing.
        Assert.True(ownerAwareConstructions >= 3,
            $"Expected the owner-aware constructions of Order/Customer/Product in production code, found {ownerAwareConstructions}. " +
            "The newobj IL scan may be broken or the create handlers refactored.");

        Assert.True(failures.Count == 0,
            "Owner-scoped aggregates must be constructed with explicit owner scope in production code:\n" +
            string.Join("\n", failures));
    }

    // Walk up nested compiler-generated types (state machines, lambda display classes) to the
    // real declaring type, so failure messages name the handler rather than a synthetic class.
    private static Type OutermostType(Type type)
    {
        while (type.IsNested && type.DeclaringType is { } parent)
            type = parent;
        return type;
    }

    // Resolve constructors of the named owned aggregates targeted by `newobj` (0x73) opcodes.
    private static IEnumerable<ConstructorInfo> ResolveConstructedAggregates(MethodInfo method, string[] aggregateNames)
    {
        var body = method.GetMethodBody();
        if (body == null)
            yield break;

        var il = body.GetILAsByteArray();
        if (il == null)
            yield break;

        var module = method.Module;

        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] != 0x73) // newobj
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            ConstructorInfo? ctor = null;
            try
            {
                ctor = module.ResolveMethod(token) as ConstructorInfo;
            }
            catch
            {
                // Unresolvable generic instantiation / non-method token — skip.
            }

            if (ctor?.DeclaringType is { } declaring && aggregateNames.Contains(declaring.Name))
                yield return ctor;

            i += 4;
        }
    }

    [Fact]
    public void ApiRouteEndpoints_MustRequireGatewayIdentity()
    {
        using var app = BuildEndpointMetadataApp();
        var failures = GetApiRouteEndpoints(app)
            .Where(endpoint => endpoint.Metadata.GetMetadata<GatewayIdentityRequiredMetadata>() == null)
            .Select(endpoint => $"{FormatEndpoint(endpoint)} must call RequireGatewayIdentity().")
            .ToList();

        Assert.True(failures.Count == 0,
            "API endpoint routes must opt into the trusted gateway identity middleware:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ApiRouteEndpoints_MustRequireGatewayScope()
    {
        using var app = BuildEndpointMetadataApp();
        var failures = GetApiRouteEndpoints(app)
            .Where(endpoint => endpoint.Metadata.GetMetadata<GatewayScopeRequiredMetadata>() == null)
            .Select(endpoint => $"{FormatEndpoint(endpoint)} must call RequireScope(\"...\").")
            .ToList();

        Assert.True(failures.Count == 0,
            "API endpoint routes must declare the required gateway scope:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ApiWriteEndpoints_MustBeSecuredBy2Fa()
    {
        using var app = BuildEndpointMetadataApp();
        var failures = GetApiRouteEndpoints(app)
            .Where(IsWriteEndpoint)
            .Where(endpoint => endpoint.Metadata.GetMetadata<GatewayTwoFactorRequiredMetadata>() == null)
            .Select(endpoint => $"{FormatEndpoint(endpoint)} must call SecuredBy2Fa().")
            .ToList();

        Assert.True(failures.Count == 0,
            "API write routes must require gateway-projected two-factor authentication:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void GatewayIdentityHeaders_MustOnlyBeReadByIdentityInfrastructure()
    {
        var gatewayHeaderLiteralFailures = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !IsCompilerGenerated(t) && !IsIdentityInfrastructure(t))
            .SelectMany(type => ExtractStringLiterals(type)
                .Where(IsGatewayIdentityHeaderLiteral)
                .Select(literal => $"{type.FullName} embeds gateway identity header literal '{FormatHeaderLiteral(literal)}'."))
            .ToList();

        var gatewayHeaderTypeFailures = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !IsCompilerGenerated(t) && !IsIdentityInfrastructure(t))
            .Where(type => GetAllMethodsIncludingStateMachines(type)
                .Any(method => IlReferencesType(method, nameof(GatewayIdentityHeaders))))
            .Select(type => $"{type.FullName} references {nameof(GatewayIdentityHeaders)} directly.")
            .ToList();

        var failures = gatewayHeaderLiteralFailures
            .Concat(gatewayHeaderTypeFailures)
            .OrderBy(message => message, StringComparer.Ordinal)
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

    private static bool IsIdentityInfrastructure(Type type)
    {
        return type.Namespace?.StartsWith("StarterApp.Api.Infrastructure.Identity", StringComparison.Ordinal) == true;
    }

    private static bool IsGatewayIdentityHeaderLiteral(string literal)
    {
        return literal.Contains("X-Authenticated-", StringComparison.Ordinal) ||
               literal.Contains("X-Gateway-Assertion", StringComparison.Ordinal);
    }

    private static string FormatHeaderLiteral(string literal)
    {
        return literal.Length <= 80 ? literal : literal[..77] + "...";
    }

    private static WebApplication BuildEndpointMetadataApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddSingleton<IMediator, EndpointMetadataMediator>();
        var app = builder.Build();
        app.MapApiEndpoints();
        return app;
    }

    private static IReadOnlyList<RouteEndpoint> GetApiRouteEndpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true)
            .ToList();
    }

    private static string FormatEndpoint(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"];
        return $"{string.Join(",", methods)} {endpoint.RoutePattern.RawText}";
    }

    private static bool IsWriteEndpoint(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return methods.Count == 0 || methods.Any(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method));
    }

    private sealed class EndpointMetadataMediator : IMediator
    {
        public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Endpoint metadata convention tests never invoke handlers.");
        }

        public Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Endpoint metadata convention tests never invoke handlers.");
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
