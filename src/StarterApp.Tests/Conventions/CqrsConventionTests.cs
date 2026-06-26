namespace StarterApp.Tests.Conventions;

public partial class CqrsConventionTests : ConventionTestBase
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

    // The negative tests above stop a command handler from reaching for Dapper or a query handler from
    // reaching for the DbContext, but neither asserts the POSITIVE side of the CQRS split: a command
    // handler that injects neither (e.g. one written against IDbConnection-free Dapper helpers, or a
    // pure pass-through) would silently route writes off the EF Core path while passing every other
    // convention. This test closes that gap mechanically — every command handler must take the write
    // path through ApplicationDbContext.
    [Fact]
    public void CommandHandlers_MustDependOnApplicationDbContext()
    {
        var commandHandlers = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.Name.EndsWith("CommandHandler", StringComparison.Ordinal) &&
                   !IsCompilerGenerated(t))
            .ToList();

        // Guard against a vacuous pass: if the discovery filter ever stops matching handlers
        // (renamed suffix, moved assembly) the violation check below would silently pass.
        Assert.NotEmpty(commandHandlers);

        var violations = commandHandlers
            .Where(t => !t.GetConstructors()
                .Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(ApplicationDbContext))))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Command handlers must inject ApplicationDbContext — writes flow through EF Core, not Dapper " +
            "(CQRS: Commands → EF Core → DTOs). A handler injecting neither data-access type would otherwise " +
            "pass CommandHandlers_MustNotDependOnIDbConnection while silently bypassing the write path:\n" +
            string.Join("\n", violations));
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

    // Provably-gated sub-queries that intentionally omit owner predicates because a prior owner-scoped
    // fetch in the SAME handler gates them: an unauthorized caller gets null/empty from the gating query
    // and never reaches the sub-query. This is the ONLY sanctioned escape from per-literal owner filtering.
    // Keep it tiny, match a distinctive SQL fragment, and document why each entry is safe — a new entry is
    // a deliberate security decision, not a way to silence the test.
    private static readonly (string HandlerFullName, string SqlFragment, string Reason)[] GatedSubQueryExemptions =
    [
        (
            "StarterApp.Api.Application.Queries.GetOrderByIdQueryHandler",
            "WHERE order_id = @Id",
            "items fetch runs only after the owner-scoped order fetch (orderSql) returns non-null; a " +
            "cross-owner caller gets null before this executes, so keying by order_id alone cannot leak rows."),
    ];

    [Fact]
    public void OwnerScopedQueryHandlers_MustFilterSqlByOwnerScope()
    {
        var handlers = GetOwnerScopedQueryHandlers().ToList();
        Assert.NotEmpty(handlers);

        // Validate EACH SELECT literal independently. Joining every literal into one blob (the prior
        // approach) let a handler pass on a single owner-filtered SELECT while a SECOND, unfiltered SELECT
        // literal silently leaked cross-owner rows — the owner predicate only had to appear *somewhere*.
        var failures = new List<string>();
        foreach (var handler in handlers)
        {
            var ownedTableSelects = ExtractStringLiterals(handler)
                .Where(literal => literal.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                    && OwnedTableRegex().IsMatch(literal));

            foreach (var literal in ownedTableSelects)
            {
                if (OwnerSubjectPredicateRegex().IsMatch(literal) && TenantPredicateRegex().IsMatch(literal))
                    continue;

                var exempt = GatedSubQueryExemptions.Any(e =>
                    string.Equals(handler.FullName, e.HandlerFullName, StringComparison.Ordinal) &&
                    literal.Contains(e.SqlFragment, StringComparison.Ordinal));
                if (exempt)
                    continue;

                failures.Add($"{handler.FullName} has a SELECT over an owner-scoped table " +
                    "(customers/products/orders/order_items) that omits owner_subject = @OwnerSubject and/or " +
                    "tenant_id = @TenantId and is not a documented gated-subquery exemption.");
            }
        }

        failures = failures.Distinct().OrderBy(message => message).ToList();

        Assert.True(failures.Count == 0,
            "Owner-scoped query handlers must enforce owner filters in EVERY SELECT literal, not just one:\n" +
            string.Join("\n", failures));
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
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .Where(t => !t.GetConstructors().Any(ctor => ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IOwnerOnlyPolicy))))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Command handlers must inject IOwnerOnlyPolicy so mutations are owner-authorized consistently:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void NonCreateCommands_MustBeOwnerAuthorizedMutations()
    {
        var violations = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ICommand).IsAssignableFrom(t))
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => !t.Name.StartsWith("Create", StringComparison.Ordinal))
            .Where(t => !typeof(IOwnerAuthorizedMutation).IsAssignableFrom(t))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Non-create commands mutate an existing owner-scoped aggregate and must implement IOwnerAuthorizedMutation " +
            "so OwnerAuthorizationBehavior can verify the handler consulted IOwnerOnlyPolicy.Authorize. Creates are " +
            "exempt because they stamp ownership instead of checking it. A future non-owner-scoped command needs a " +
            "documented exemption here, not a silent omission:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void OwnerAuthorizedMutations_MustBeCommands()
    {
        var violations = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IOwnerAuthorizedMutation).IsAssignableFrom(t))
            .Where(t => !typeof(ICommand).IsAssignableFrom(t))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "IOwnerAuthorizedMutation marks commands only — queries enforce owner scope through SQL filters, " +
            "not through IOwnerOnlyPolicy.Authorize:\n" +
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

    private static IEnumerable<Type> GetOwnerScopedQueryHandlers()
    {
        return ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("QueryHandler", StringComparison.Ordinal))
            .Where(handler => handler.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) &&
                typeof(IOwnerScopedRequest).IsAssignableFrom(i.GetGenericArguments()[0])));
    }

    [GeneratedRegex(@"\b(?:\w+\.)?owner_subject\s*=\s*@OwnerSubject\b", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerSubjectPredicateRegex();

    [GeneratedRegex(@"\b(?:\w+\.)?tenant_id\s*=\s*@TenantId\b", RegexOptions.IgnoreCase)]
    private static partial Regex TenantPredicateRegex();

    [GeneratedRegex(@"\b(?:customers|products|orders|order_items)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OwnedTableRegex();

    // === Handler Wiring ===

    [Fact]
    public void EveryCommand_MustHaveAHandler()
    {
        var allHandlers = ApiAssembly
            .GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Distinct().ToArray();

        var commandsWithResponse = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i => i == typeof(ICommand)) &&
                   t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

        commandsWithResponse
            .MustConformTo(Convention.RequiresACorrespondingImplementationOf(
                typeof(IRequestHandler<,>), allHandlers))
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
                       i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));
        requestsInCommandNs
            .MustConformTo(new MustImplementMarkerInterfaceConvention(typeof(ICommand)))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void RequestsInQueryNamespace_MustImplementIQuery()
    {
        // The other direction (every IQuery is dispatchable) is a compiler guarantee now:
        // IQuery<TResult> : IRequest<TResult>. Only the cohort-escape half needs a test.
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
        protected override string FailureMessage => "must implement IRequest<T>";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            // IRequest<T> only — the void IRequest path was removed so every command/query flows
            // through the single behavior-running pipeline; commands with no result use IRequest<Unit>.
            var implementsRequest = type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

            return implementsRequest
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} must implement IRequest<T> for mediator dispatch (use IRequest<Unit> for no result)");
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
