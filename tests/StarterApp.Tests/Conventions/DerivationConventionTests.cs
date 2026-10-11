namespace StarterApp.Tests.Conventions;

// Types are matched by simple name so the rule survives the derivation's namespace rename.
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

    internal static List<string> SampleLeftovers(IEnumerable<Type> productionTypes) => productionTypes
        .Where(t => !IsCompilerGenerated(t) && !IsModuleType(t) && SampleDomainTypeNames.Contains(t.Name))
        .Select(t => t.FullName!)
        .Order(StringComparer.Ordinal)
        .ToList();

    internal static List<string> SupportNoModuleUses(IReadOnlyCollection<Type> apiTypes) => SupportMarkerNames
        .Where(name => apiTypes.Any(t => t.IsInterface && t.Name == name))
        .Where(name => !apiTypes.Any(t => IsModuleType(t) && t.GetInterfaces().Any(i => i.Name == name)))
        .ToList();

    private static readonly string SamplePage = Path.Combine("web", "apps", "web", "src", "sample");

    internal static List<string> SamplePageLeftovers(string repoRoot) =>
        Directory.Exists(Path.Combine(repoRoot, SamplePage)) ? [SamplePage] : [];

    [DerivedProjectFact]
    public void DerivedProject_WithAModule_MustNotKeepTheSampleDomain()
    {
        var leftovers = SampleLeftovers(CoreProductionAssemblies.SelectMany(a => a.GetTypes()));

        Assert.True(leftovers.Count == 0,
            "The first module has landed, so the starter's Customer/Product/Order sample is vestigial and must be " +
            "removed end to end (docs/DERIVATION-PRUNING.md):\n" + string.Join("\n", leftovers));
    }

    [DerivedProjectFact]
    public void DerivedProject_WithAModule_MustNotKeepSupportNoModuleUses()
    {
        var unused = SupportNoModuleUses(ApiAssembly.GetTypes());

        Assert.True(unused.Count == 0,
            "These support capabilities have no module consumer. Use them from a module, or remove the capability " +
            "with its behaviour, conventions and docs and record a re-add trigger (docs/DERIVATION-PRUNING.md):\n" +
            string.Join("\n", unused));
    }

    [DerivedProjectFact]
    public void DerivedProject_WithAModule_MustNotKeepTheSamplePage()
    {
        var leftovers = SamplePageLeftovers(TestPaths.RepoRoot);

        Assert.True(leftovers.Count == 0,
            "The first module has landed, so the web app's sample page over the Customer/Product/Order sample is vestigial " +
            "and goes with it; the shell, the identity helper, the API client and the browser-test fixtures stay " +
            "(docs/DERIVATION-PRUNING.md):\n" + string.Join("\n", leftovers));
    }

    [Fact]
    public void TheSamplePageRule_BitesOnASyntheticDerivedProject()
    {
        var root = Directory.CreateTempSubdirectory("derivation-").FullName;
        try
        {
            Assert.Empty(SamplePageLeftovers(root));
            Directory.CreateDirectory(Path.Combine(root, SamplePage));
            Assert.Equal([SamplePage], SamplePageLeftovers(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

[AttributeUsage(AttributeTargets.Method)]
public sealed class DerivedProjectFactAttribute : FactAttribute
{
    public const string NotApplicable =
        "Not applicable in the template: no module under Api/Modules yet, so there is nothing to prune (docs/DERIVATION-PRUNING.md).";

    public static bool HasModules { get; } =
        typeof(IApiMarker).Assembly.GetTypes().Any(type => type.Namespace?.Split('.').Contains("Modules") == true);

    public DerivedProjectFactAttribute()
    {
        if (!HasModules)
            Skip = NotApplicable;
    }
}
