using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Infrastructure.Mediator;
using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Tests.Conventions;

public class NamingConventionTests : ConventionTestBase
{
    [Fact]
    public void EndpointDefinitions_ShouldFollowNamingConvention()
    {
        var endpointTypes = ApiAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.Name == "IEndpointDefinition"));

        endpointTypes
            .MustConformTo(Convention.NameMustEndWith("Endpoints"))
            .WithFailureAssertion(Assert.Fail);
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

    [Fact]
    public void ApplicationContracts_ShouldLiveInExpectedNamespaces()
    {
        var failures = new List<string>();

        AddNamespaceFailures(
            failures,
            ApiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i => i == typeof(ICommand))),
            "StarterApp.Api.Application.Commands");

        AddNamespaceFailures(
            failures,
            ApiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))),
            "StarterApp.Api.Application.Queries");

        AddNamespaceFailures(
            failures,
            ApiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))),
            "StarterApp.Api.Application.Validators");

        AddNamespaceFailures(
            failures,
            ApiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Dto")),
            "StarterApp.Api.Application.DTOs");

        AddNamespaceFailures(
            failures,
            ApiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("ReadModel")),
            "StarterApp.Api.Application.ReadModels");

        Assert.True(failures.Count == 0,
            "Application contracts must live in their mechanically discoverable namespaces:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void ApplicationHandlers_ShouldLiveBesideHandledContracts()
    {
        var failures = new List<string>();

        var handlerTypes = ApiAssembly.GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<,>))
            .Concat(ApiAssembly.GetAllTypesImplementingOpenGenericType(typeof(IRequestHandler<>)))
            .Distinct();

        foreach (var handler in handlerTypes)
        {
            var requestType = GetHandledRequestType(handler);
            if (requestType == null)
                continue;

            var expectedNamespace = requestType.GetInterfaces().Any(i => i == typeof(ICommand))
                ? "StarterApp.Api.Application.Commands"
                : requestType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))
                    ? "StarterApp.Api.Application.Queries"
                    : null;

            if (expectedNamespace != null && handler.Namespace != expectedNamespace)
                failures.Add($"{handler.FullName} handles {requestType.Name} and must live in {expectedNamespace}.");
        }

        Assert.True(failures.Count == 0,
            "Application handlers must live beside their command/query contract:\n" +
            string.Join("\n", failures));
    }

    private static void AddNamespaceFailures(List<string> failures, IEnumerable<Type> types, string expectedNamespace)
    {
        failures.AddRange(types
            .Where(t => !IsCompilerGenerated(t))
            .Where(t => t.Namespace != expectedNamespace)
            .Select(t => $"{t.FullName} must live in {expectedNamespace}."));
    }

    private static Type? GetHandledRequestType(Type handlerType)
    {
        return handlerType.GetInterfaces()
            .Where(i => i.IsGenericType)
            .Where(i =>
                i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                i.GetGenericTypeDefinition() == typeof(IRequestHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }
}
