using Conventional;
using Conventional.Conventions;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Data;
using StarterApp.Api.Infrastructure.Mediator;

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
