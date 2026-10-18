namespace StarterApp.Tests.Conventions;

public class SharedKernelConventionTests : ConventionTestBase
{
    private static readonly string[] KernelNamespaces = ["Abstractions", "Exceptions", "ValueObjects"];

    private static List<Type> KernelTypes() => SharedKernelAssembly.GetTypes()
        .Where(t => !IsCompilerGenerated(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    [Fact]
    public void SharedKernel_HoldsOnlyAbstractionsExceptionsAndValueObjects()
    {
        var strays = KernelTypes()
            .Where(t => !KernelNamespaces.Contains(t.Namespace?.Split('.').Last()))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(strays.Count == 0,
            "The shared kernel holds base types, domain exceptions and generic value objects only. A domain type " +
            "belongs in the domain (or module) that owns it:\n" + string.Join("\n", strays));
    }

    [Fact]
    public void SharedKernel_HoldsNoAggregateAndNoEvent()
    {
        var domainTypes = KernelTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        (t.IsSubclassOf(typeof(AggregateRoot)) || typeof(IDomainEvent).IsAssignableFrom(t)))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(domainTypes.Count == 0,
            "An aggregate or a concrete domain event belongs in the domain (or module) that owns it, never in the shared kernel:\n" +
            string.Join("\n", domainTypes));
    }

    [Fact]
    public void SharedKernelAbstractions_AreAbstractInterfacesOrStatic()
    {
        var concrete = KernelTypes()
            .Where(t => t.Namespace!.EndsWith("Abstractions", StringComparison.Ordinal) && t.IsClass && !t.IsAbstract)
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(concrete.Count == 0,
            "Abstractions/ holds base classes, interfaces and static defaults. A concrete instantiable class there is a domain type in disguise:\n" +
            string.Join("\n", concrete));
    }

    [Fact]
    public void SharedKernel_MustNotReferenceTheDomainAssembly()
    {
        var referenced = SharedKernelAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(DomainAssembly.GetName().Name, referenced);
    }
}
