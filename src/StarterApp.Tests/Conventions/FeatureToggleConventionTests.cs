namespace StarterApp.Tests.Conventions;

public class FeatureToggleConventionTests : ConventionTestBase
{
    [Fact]
    public void FeatureToggles_MustBeOnRequestTypes_NeverOnHandlers()
    {
        var violations = ApiAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<FeatureToggleAttribute>(inherit: false) is not null)
            .Where(t => !t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.True(violations.Count == 0,
            "[FeatureToggle] belongs on request types (commands/queries) so the mediator can refuse dispatch " +
            "centrally — never on handlers or other types:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void FeatureToggleNames_MustBeUnique()
    {
        var duplicates = ApiAssembly.GetTypes()
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<FeatureToggleAttribute>(inherit: false)))
            .Where(x => x.Attribute is not null)
            .GroupBy(x => x.Attribute!.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"  '{g.Key}' is declared by: {string.Join(", ", g.Select(x => x.Type.Name))}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Feature toggle names must be unique — a shared name silently couples unrelated features:\n" +
            string.Join("\n", duplicates));
    }

    [Fact]
    public void FeatureToggleNames_MustHaveExplicitConfigurationEntries()
    {
        var declared = ApiAssembly.GetTypes()
            .Select(t => t.GetCustomAttribute<FeatureToggleAttribute>(inherit: false))
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (declared.Count == 0)
            return;

        var appsettingsPath = Path.Combine(RepoRoot(), "src", "StarterApp.Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var configured = document.RootElement.TryGetProperty("FeatureToggles", out var section) && section.ValueKind == JsonValueKind.Object
            ? section.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : [];

        var missing = declared.Where(name => !configured.Contains(name)).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0,
            "Every declared feature toggle needs an explicit entry in src/StarterApp.Api/appsettings.json " +
            "under 'FeatureToggles' — the default state must be a reviewed decision, not an accident of a " +
            "missing key:\n" + string.Join("\n", missing));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StarterApp.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
    }
}
