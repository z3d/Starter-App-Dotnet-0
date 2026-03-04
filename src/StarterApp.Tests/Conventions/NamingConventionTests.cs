using Conventional;

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
}
