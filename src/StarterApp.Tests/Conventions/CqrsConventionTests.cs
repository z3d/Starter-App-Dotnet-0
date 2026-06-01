namespace StarterApp.Tests.Conventions;

public class CqrsConventionTests : ConventionTestBase
{
    // === Data Access Separation ===

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

    [Fact]
    public void ResourceQueries_MustBeOwnerScoped()
    {
        var queryTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)))
            .Where(t => !typeof(IOwnerScopedRequest).IsAssignableFrom(t))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(queryTypes.Count == 0,
            "Resource queries must implement IOwnerScopedRequest so reads cannot drift back to global visibility:\n" +
            string.Join("\n", queryTypes));
    }

    [Fact]
    public void QueryHandlers_MustInjectOwnerOnlyPolicy()
    {
        var violations = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("QueryHandler", StringComparison.Ordinal))
            .Where(t => !t.GetConstructors().Any(ctor => ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IOwnerOnlyPolicy))))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Query handlers must inject IOwnerOnlyPolicy so reads are filtered by the current owner scope:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void CommandHandlers_MustInjectOwnerOnlyPolicy()
    {
        var violations = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Concat(ApiAssembly.GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<>)))
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .Where(t => !t.GetConstructors().Any(ctor => ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IOwnerOnlyPolicy))))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Command handlers must inject IOwnerOnlyPolicy so mutations are owner-authorized consistently:\n" +
            string.Join("\n", violations));
    }

    // Injecting IOwnerOnlyPolicy is necessary but not sufficient — a handler that injects it and
    // never calls it provides zero owner authorization. These tests scan handler IL (including async
    // state machines) for an actual call to a member declared on IOwnerOnlyPolicy, closing the gap
    // where the injection tests above could pass for an inject-and-forget handler.

    [Fact]
    public void CommandHandlers_MustInvokeOwnerOnlyPolicy()
    {
        var handlers = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Concat(ApiAssembly.GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<>)))
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .Where(InjectsOwnerOnlyPolicy)
            .ToList();

        AssertInvokesOwnerOnlyPolicy(handlers, "Command");
    }

    [Fact]
    public void QueryHandlers_MustInvokeOwnerOnlyPolicy()
    {
        var handlers = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("QueryHandler", StringComparison.Ordinal))
            .Where(InjectsOwnerOnlyPolicy)
            .ToList();

        AssertInvokesOwnerOnlyPolicy(handlers, "Query");
    }

    private static bool InjectsOwnerOnlyPolicy(Type handler)
    {
        return handler.GetConstructors()
            .Any(ctor => ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IOwnerOnlyPolicy)));
    }

    private static void AssertInvokesOwnerOnlyPolicy(IReadOnlyCollection<Type> handlers, string kind)
    {
        Assert.NotEmpty(handlers);

        var violations = handlers
            .Where(handler => !GetAllMethodsIncludingStateMachines(handler)
                .Any(method => IlReferencesType(method, nameof(IOwnerOnlyPolicy))))
            .Select(handler => handler.FullName ?? handler.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            $"{kind} handlers that inject IOwnerOnlyPolicy must actually call it (Authorize/GetRequiredScope) — " +
            "injecting the policy without invoking it provides no owner authorization:\n" +
            string.Join("\n", violations));
    }

    // === Handler Wiring ===

    [Fact]
    public void EveryCommand_MustHaveAHandler()
    {
        var allHandlers = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Concat(ApiAssembly.GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<>)))
            .Distinct().ToArray();

        var commandsWithResponse = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(ICommand)) &&
                   t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

        var commandsVoid = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(ICommand)) &&
                   t.GetInterfaces().Any(i => i == typeof(IRequest)));

        commandsWithResponse
            .MustConformTo(Convention.RequiresACorrespondingImplementationOf(
                typeof(IRequestHandler<,>), allHandlers))
            .WithFailureAssertion(Assert.Fail);

        commandsVoid
            .MustConformTo(Convention.RequiresACorrespondingImplementationOf(
                typeof(IRequestHandler<>), allHandlers))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void EveryQuery_MustHaveAHandler()
    {
        var allHandlers = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .ToArray();

        var queryTypes = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IQuery<>));

        queryTypes
            .MustConformTo(Convention.RequiresACorrespondingImplementationOf(
                typeof(IRequestHandler<,>), allHandlers))
            .WithFailureAssertion(Assert.Fail);
    }

    // === Validator Coverage ===
    // AI-agent maintained: mechanical rule eliminates judgment calls about which commands "need" validators.
    // Agents generate boilerplate cheaply; ambiguity about coverage is the real risk.

    [Fact]
    public void EveryCommand_MustHaveAValidator()
    {
        var validatorTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t))
            .ToArray();

        var commandTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(ICommand)));

        foreach (var command in commandTypes)
        {
            var expectedValidator = typeof(IValidator<>).MakeGenericType(command);
            var hasValidator = validatorTypes.Any(t => expectedValidator.IsAssignableFrom(t));
            Assert.True(hasValidator,
                $"Command {command.Name} must have a validator implementing IValidator<{command.Name}>. " +
                "This codebase is AI-agent maintained — every command requires a validator for structured error responses.");
        }
    }

    [Fact]
    public void EveryQuery_MustHaveAValidator()
    {
        var validatorTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t))
            .ToArray();

        var queryTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)));

        foreach (var query in queryTypes)
        {
            var expectedValidator = typeof(IValidator<>).MakeGenericType(query);
            var hasValidator = validatorTypes.Any(t => expectedValidator.IsAssignableFrom(t));
            Assert.True(hasValidator,
                $"Query {query.Name} must have a validator implementing IValidator<{query.Name}>. " +
                "This codebase is AI-agent maintained — every query requires a validator for structured error responses.");
        }
    }

    // === Dual Interface Enforcement ===

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
}
