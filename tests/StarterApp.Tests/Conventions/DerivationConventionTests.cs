namespace StarterApp.Tests.Conventions;

// The template carries no modules, so both facts pass here and bite only in a derived project: the
// moment its first module lands under Api/Modules, the sample domain and any support capability no
// module uses are vestigial (docs/DERIVATION-PRUNING.md). Types are matched by simple name so the
// rule survives the derivation's namespace rename.
public class DerivationConventionTests : ConventionTestBase
{
    private static readonly string[] SampleDomainTypeNames =
    [
        "Customer", "Product", "Order", "OrderItem", "OrderStatus",
        "OrderCreatedDomainEvent", "OrderStatusChangedDomainEvent",
        "OrderConfirmationEmailFunction", "InventoryReservationFunction"
    ];

    private static readonly string[] SupportMarkerNames = ["ICacheable", "IOwnerScopedRequest", "IOwnerAuthorizedMutation"];

    private static bool IsModuleType(Type type) =>
        type.Namespace?.Split('.').Contains("Modules") == true;

    private static bool HasModules() => ApiAssembly.GetTypes().Any(IsModuleType);

    internal static List<string> SampleLeftovers(IEnumerable<Type> productionTypes) => productionTypes
        .Where(t => !IsCompilerGenerated(t) && !IsModuleType(t) && SampleDomainTypeNames.Contains(t.Name))
        .Select(t => t.FullName!)
        .Order(StringComparer.Ordinal)
        .ToList();

    internal static List<string> SupportNoModuleUses(IReadOnlyCollection<Type> apiTypes) => SupportMarkerNames
        .Where(name => apiTypes.Any(t => t.IsInterface && t.Name == name))
        .Where(name => !apiTypes.Any(t => IsModuleType(t) && t.GetInterfaces().Any(i => i.Name == name)))
        .ToList();

    [Fact]
    public void DerivedProject_WithAModule_MustNotKeepTheSampleDomain()
    {
        if (!HasModules())
            return;

        var leftovers = SampleLeftovers(CoreProductionAssemblies.SelectMany(a => a.GetTypes()));

        Assert.True(leftovers.Count == 0,
            "The first module has landed, so the starter's Customer/Product/Order sample is vestigial and must be " +
            "removed end to end (docs/DERIVATION-PRUNING.md):\n" + string.Join("\n", leftovers));
    }

    [Fact]
    public void DerivedProject_WithAModule_MustNotKeepSupportNoModuleUses()
    {
        if (!HasModules())
            return;

        var unused = SupportNoModuleUses(ApiAssembly.GetTypes());

        Assert.True(unused.Count == 0,
            "These support capabilities have no module consumer. Use them from a module, or remove the capability " +
            "with its behaviour, conventions and docs and record a re-add trigger (docs/DERIVATION-PRUNING.md):\n" +
            string.Join("\n", unused));
    }

    [Fact]
    public void BothRules_BiteOnASyntheticDerivedProject()
    {
        var derived = typeof(SyntheticDerivation.Modules.Billing.Invoice).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(typeof(SyntheticDerivation.Customer).Namespace!, StringComparison.Ordinal) == true)
            .ToList();

        Assert.Contains(derived, IsModuleType);
        Assert.Equal([typeof(SyntheticDerivation.Customer).FullName!, typeof(SyntheticDerivation.OrderCreatedDomainEvent).FullName!], SampleLeftovers(derived));
        Assert.Equal(["IOwnerScopedRequest"], SupportNoModuleUses(derived));
    }
}
