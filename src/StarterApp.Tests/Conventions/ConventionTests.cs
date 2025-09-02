using Conventional;
using StarterApp.Api.Infrastructure;

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
    public void ControllersShouldFollowNamingConvention()
    {
        var controllerTypes = ApiAssembly.GetTypes().Where(t => t.Name.EndsWith("Controller"));
        controllerTypes.MustConformTo(Convention.NameMustEndWith("Controller"));
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
                       !typeof(ControllerBase).IsAssignableFrom(t) && // Exclude controllers
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
    public void Repositories_ShouldFollowNamingConventions()
    {
        var repositoryTypes = ApiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Repositories") &&
                   t.IsClass && !t.IsAbstract &&
                   !IsCompilerGenerated(t));
        repositoryTypes
            .MustConformTo(Convention.NameMustEndWith("Repository"))
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void TestClasses_ShouldFollowNamingConventions()
    {
        var testTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetMethods().Any(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any()));

        testTypes
            .MustConformTo(Convention.NameMustEndWith("Tests").Or(Convention.NameMustEndWith("Test")))
            .WithFailureAssertion(Assert.Fail);
    }
}




