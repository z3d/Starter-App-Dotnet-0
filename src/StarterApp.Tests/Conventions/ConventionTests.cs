using Conventional;
using Conventional.Conventions;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Data;
using StarterApp.Api.Infrastructure;
using StarterApp.Api.Infrastructure.Mediator;

namespace StarterApp.Tests.Conventions;

public class ConventionTests
{
    private static readonly Assembly DomainAssembly = typeof(Product).Assembly;
    private static readonly Assembly ApiAssembly = typeof(IApiMarker).Assembly;

    private static bool IsCompilerGenerated(Type type)
    {
        return type.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Any() ||
               type.Name.Contains("<") ||
               type.Name.Contains(">") ||
               type.Name.StartsWith("<>") ||
               type.Name.Contains("d__") ||
               type.Name.Contains("c__DisplayClass") ||
               type.Name.Contains("__StaticArrayInitTypeSize") ||
               type.IsNested; // Often compiler-generated types are nested
    }

    [Fact]
    public void EndpointDefinitions_ShouldFollowNamingConvention()
    {
        var endpointTypes = ApiAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.Name == "IEndpointDefinition"));

        endpointTypes.MustConformTo(Convention.NameMustEndWith("Endpoints"));
    }

    [Fact]
    public void DTOs_ShouldFollowNamingConventions()
    {
        var dtoTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null &&
                   (t.Namespace.Contains("DTOs") || t.Namespace.Contains("ReadModels")) &&
                   t.IsClass && !t.IsAbstract);
        dtoTypes
            .MustConformTo(Convention.NameMustEndWith("Dto").Or(Convention.NameMustEndWith("ReadModel")))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void Commands_ShouldFollowNamingConventions()
    {
        var commandTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Commands") &&
                   t.IsClass && !t.IsAbstract &&
                   !t.Name.EndsWith("Handler") && !t.Name.EndsWith("Service") &&
                   !IsCompilerGenerated(t));
        commandTypes
            .MustConformTo(Convention.NameMustEndWith("Command"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void Queries_ShouldFollowNamingConventions()
    {
        var queryTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Queries") &&
                   t.IsClass && !t.IsAbstract &&
                   !t.Name.EndsWith("Handler") && !t.Name.EndsWith("Service") &&
                   !IsCompilerGenerated(t));
        queryTypes
            .MustConformTo(Convention.NameMustEndWith("Query"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void DomainEntities_ShouldHaveProperEncapsulation()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract);
        entityTypes
            .MustConformTo(Convention.PropertiesMustHavePrivateSetters)
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void ValueObjects_ShouldBeImmutable()
    {
        var valueObjectTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("ValueObjects") &&
                   t.IsClass && !t.IsAbstract);
        valueObjectTypes
            .MustConformTo(Convention.PropertiesMustHavePrivateSetters)
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void DTOs_ShouldHavePublicGetters()
    {
        var dtoTypes = ApiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Dto") || t.Name.EndsWith("ReadModel"));
        dtoTypes
            .MustConformTo(Convention.PropertiesMustHavePublicGetters)
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void AsyncMethods_ShouldHaveAsyncSuffix()
    {
        var assemblies = new[] { ApiAssembly, DomainAssembly };

        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                       !t.Name.EndsWith("Handler") && // Exclude command/query handlers
                       !IsCompilerGenerated(t));
            types
                .MustConformTo(Convention.AsyncMethodsMustHaveAsyncSuffix)
                .WithFailureAssertion(Assert.Fail);
        }
    }

    [Fact]
    public void Services_ShouldFollowNamingConventions()
    {
        var serviceTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Services") &&
                   t.IsClass && !t.IsAbstract &&
                   !IsCompilerGenerated(t));
        serviceTypes
            .MustConformTo(Convention.NameMustEndWith("Service"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void Validators_ShouldFollowNamingConventions()
    {
        var validatorTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Validators") &&
                   t.IsClass && !t.IsAbstract &&
                   !IsCompilerGenerated(t));
        validatorTypes
            .MustConformTo(Convention.NameMustEndWith("Validator"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void CommandHandlers_ShouldFollowNamingConventions()
    {
        var handlerTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Commands") &&
                   t.IsClass && !t.IsAbstract &&
                   t.Name.EndsWith("Handler") &&
                   !IsCompilerGenerated(t));
        handlerTypes
            .MustConformTo(Convention.NameMustEndWith("CommandHandler"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void QueryHandlers_ShouldFollowNamingConventions()
    {
        var handlerTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Queries") &&
                   t.IsClass && !t.IsAbstract &&
                   t.Name.EndsWith("Handler") &&
                   !IsCompilerGenerated(t));
        handlerTypes
            .MustConformTo(Convention.NameMustEndWith("QueryHandler"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void TestClasses_ShouldFollowNamingConventions()
    {
        var propertyAttributeType = Type.GetType("FsCheck.Xunit.PropertyAttribute, FsCheck.Xunit");

        var testTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetMethods().Any(m =>
                       m.GetCustomAttributes(typeof(FactAttribute), false).Any() ||
                       (propertyAttributeType != null && m.GetCustomAttributes(propertyAttributeType, false).Any())));

        testTypes
            .MustConformTo(Convention.NameMustEndWith("Tests").Or(Convention.NameMustEndWith("Test")))
            .WithFailureAssertion(Assert.Fail);
    }

    // === CQRS Data Access Separation ===

    [Fact]
    public void CommandHandlers_MustNotDependOnIDbConnection()
    {
        var commandHandlers = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.Name.EndsWith("CommandHandler") &&
                   !IsCompilerGenerated(t));
        commandHandlers
            .MustConformTo(Convention.MustNotTakeADependencyOn(typeof(System.Data.IDbConnection), "Commands should use ApplicationDbContext for writes"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void QueryHandlers_MustNotDependOnDbContext()
    {
        var queryHandlers = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.Name.EndsWith("QueryHandler") &&
                   !IsCompilerGenerated(t));
        queryHandlers
            .MustConformTo(Convention.MustNotTakeADependencyOn(typeof(ApplicationDbContext), "Queries should use IDbConnection/Dapper for reads"))
            .WithFailureAssertion(Assert.Fail);
    }

    // === Domain Model Integrity ===

    [Fact]
    public void DomainEntities_MustHaveNonPublicDefaultConstructor()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));
        entityTypes
            .MustConformTo(Convention.MustHaveANonPublicDefaultConstructor)
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void ValueObjects_MustOverrideEqualsAndGetHashCode()
    {
        var valueObjectTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("ValueObjects") &&
                   t.IsClass && !t.IsAbstract);
        valueObjectTypes
            .MustConformTo(new MustOverrideEqualsAndGetHashCodeConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    // === CQRS Handler Wiring ===

    [Fact]
    public void EveryCommand_MustHaveExactlyOneHandler()
    {
        var commandTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(ICommand)));

        var allHandlerTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType &&
                       (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                        i.GetGenericTypeDefinition() == typeof(IRequestHandler<>))))
            .ToArray();

        commandTypes
            .MustConformTo(new MustHaveCorrespondingHandlerConvention(allHandlerTypes))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void EveryQuery_MustHaveExactlyOneHandler()
    {
        var queryTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType &&
                       i.GetGenericTypeDefinition() == typeof(IQuery<>)));

        var allHandlerTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType &&
                       i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .ToArray();

        queryTypes
            .MustConformTo(new MustHaveCorrespondingHandlerConvention(allHandlerTypes))
            .WithFailureAssertion(Assert.Fail);
    }

    // === CQRS Dual Interface Enforcement ===

    [Fact]
    public void Commands_MustImplementBothICommandAndIRequest()
    {
        var commandTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(ICommand)));
        commandTypes
            .MustConformTo(new MustImplementRequestInterfaceConvention())
            .WithFailureAssertion(Assert.Fail);

        var requestsInCommandNs = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.Namespace != null && t.Namespace.Contains("Commands") &&
                   !t.Name.EndsWith("Handler") && !IsCompilerGenerated(t) &&
                   t.GetInterfaces().Any(i =>
                       (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)) ||
                       i == typeof(IRequest)));
        requestsInCommandNs
            .MustConformTo(new MustImplementMarkerInterfaceConvention(typeof(ICommand)))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void Queries_MustImplementBothIQueryAndIRequest()
    {
        var queryTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType &&
                       i.GetGenericTypeDefinition() == typeof(IQuery<>)));
        queryTypes
            .MustConformTo(new MustImplementRequestInterfaceConvention())
            .WithFailureAssertion(Assert.Fail);

        var requestsInQueryNs = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.Namespace != null && t.Namespace.Contains("Queries") &&
                   !t.Name.EndsWith("Handler") && !IsCompilerGenerated(t) &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));
        requestsInQueryNs
            .MustConformTo(new MustImplementGenericMarkerInterfaceConvention(typeof(IQuery<>)))
            .WithFailureAssertion(Assert.Fail);
    }

    // === Custom Convention Specifications ===

    private class MustHaveCorrespondingHandlerConvention : ConventionSpecification
    {
        private readonly Type[] _allHandlerTypes;

        public MustHaveCorrespondingHandlerConvention(Type[] allHandlerTypes)
        {
            _allHandlerTypes = allHandlerTypes;
        }

        protected override string FailureMessage => "must have exactly one corresponding handler";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var requestWithResponse = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
            var requestVoid = type.GetInterfaces()
                .FirstOrDefault(i => i == typeof(IRequest));

            Type expectedHandlerInterface;
            if (requestWithResponse != null)
            {
                var responseType = requestWithResponse.GetGenericArguments()[0];
                expectedHandlerInterface = typeof(IRequestHandler<,>).MakeGenericType(type, responseType);
            }
            else if (requestVoid != null)
            {
                expectedHandlerInterface = typeof(IRequestHandler<>).MakeGenericType(type);
            }
            else
            {
                return ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} does not implement IRequest<T> or IRequest");
            }

            var matchingHandlers = _allHandlerTypes
                .Where(h => h.GetInterfaces().Any(i => i == expectedHandlerInterface))
                .ToList();

            if (matchingHandlers.Count == 0)
                return ConventionResult.NotSatisfied(type.FullName!,
                    $"No handler found for {type.Name}");

            if (matchingHandlers.Count > 1)
                return ConventionResult.NotSatisfied(type.FullName!,
                    $"Multiple handlers for {type.Name}: {string.Join(", ", matchingHandlers.Select(h => h.Name))}");

            return ConventionResult.Satisfied(type.FullName!);
        }
    }

    private class MustImplementRequestInterfaceConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must implement IRequest<T> or IRequest";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var implementsRequest = type.GetInterfaces().Any(i =>
                (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)) ||
                i == typeof(IRequest));

            return implementsRequest
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} must implement IRequest<T> or IRequest for mediator dispatch");
        }
    }

    private class MustImplementMarkerInterfaceConvention : ConventionSpecification
    {
        private readonly Type _markerInterface;

        public MustImplementMarkerInterfaceConvention(Type markerInterface)
        {
            _markerInterface = markerInterface;
        }

        protected override string FailureMessage => $"must implement {_markerInterface.Name}";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            return type.GetInterfaces().Any(i => i == _markerInterface)
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} must implement {_markerInterface.Name}");
        }
    }

    private class MustImplementGenericMarkerInterfaceConvention : ConventionSpecification
    {
        private readonly Type _genericInterfaceDefinition;

        public MustImplementGenericMarkerInterfaceConvention(Type genericInterfaceDefinition)
        {
            _genericInterfaceDefinition = genericInterfaceDefinition;
        }

        protected override string FailureMessage => $"must implement {_genericInterfaceDefinition.Name}";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            return type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == _genericInterfaceDefinition)
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} must implement {_genericInterfaceDefinition.Name}");
        }
    }

    private class MustOverrideEqualsAndGetHashCodeConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must override Equals(object) and GetHashCode()";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var equalsMethod = type.GetMethod("Equals", [typeof(object)]);
            var getHashCodeMethod = type.GetMethod("GetHashCode", Type.EmptyTypes);

            var failures = new List<string>();

            if (equalsMethod == null || equalsMethod.DeclaringType != type)
                failures.Add($"{type.Name} does not override Equals(object)");

            if (getHashCodeMethod == null || getHashCodeMethod.DeclaringType != type)
                failures.Add($"{type.Name} does not override GetHashCode()");

            return failures.Count == 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!, string.Join("; ", failures));
        }
    }
}
