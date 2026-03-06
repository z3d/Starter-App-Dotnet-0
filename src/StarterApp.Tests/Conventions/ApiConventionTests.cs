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
        var dtoTypes = ApiAssembly.GetTypes()
            .Where(t => (t.Name.EndsWith("Dto") || t.Name.EndsWith("ReadModel")) &&
                   t.IsClass && !t.IsAbstract);

        dtoTypes
            .MustConformTo(new MustNotHaveInstanceMethodsConvention())
            .WithFailureAssertion(Assert.Fail);
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

    // === Custom Convention Specifications ===

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
