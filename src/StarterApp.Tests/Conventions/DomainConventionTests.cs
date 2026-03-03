using Conventional;
using Conventional.Conventions;

namespace StarterApp.Tests.Conventions;

public class DomainConventionTests : ConventionTestBase
{
    // === Encapsulation ===

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

    // === Constructors ===

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

    // === Equality ===

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

    // === Async Methods ===

    [Fact]
    public void AsyncMethods_ShouldHaveAsyncSuffix()
    {
        var assemblies = new[] { ApiAssembly, DomainAssembly };

        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                       !t.Name.EndsWith("Handler") &&
                       !IsCompilerGenerated(t));
            types
                .MustConformTo(Convention.AsyncMethodsMustHaveAsyncSuffix)
                .WithFailureAssertion(Assert.Fail);
        }
    }

    // === Custom Convention Specifications ===

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
