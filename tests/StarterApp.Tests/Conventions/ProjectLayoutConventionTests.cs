namespace StarterApp.Tests.Conventions;

// Moving code between projects breaks two things without a compile error naming the cause: an InternalsVisibleTo left on the old assembly, and a test that builds a path to the old folder, finds no files and passes having checked nothing.
public partial class ProjectLayoutConventionTests : ConventionTestBase
{
    private static string AppPrefix => ApplicationAssembly.GetName().Name!.Split('.')[0];

    [Fact]
    public void EveryInternalsVisibleTo_NamesAProjectOfTheSolution()
    {
        var granted = CoreProductionAssemblies
            .SelectMany(assembly => assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => (From: assembly.GetName().Name!, To: attribute.AssemblyName)))
            .ToList();
        Assert.NotEmpty(granted);

        var failures = UnknownGrantees(granted, SolutionProjects());

        Assert.True(failures.Count == 0, "InternalsVisibleTo names an assembly the solution does not build:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void TheGranteeRule_CatchesAMovedAssembly()
    {
        var failures = UnknownGrantees([("Kernel", "StarterApp.Gone")], SolutionProjects());

        Assert.Equal(["Kernel shows its internals to StarterApp.Gone"], failures);
    }

    [Fact]
    public void TheDomainAndTheKernel_ShowTheirInternalsToTheApplication()
    {
        var application = ApplicationAssembly.GetName().Name;

        Assert.All(new[] { SharedKernelAssembly, DomainAssembly }, assembly =>
            Assert.Contains(assembly.GetCustomAttributes<InternalsVisibleToAttribute>(), attribute => attribute.AssemblyName == application));
    }

    [Fact]
    public void EveryPathATestBuildsIntoTheSolution_Exists()
    {
        var tests = Path.Combine(TestPaths.RepoRoot, "tests");
        var built = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(path => SolutionPathsIn(File.ReadAllText(path)).Select(parts => (File: Path.GetRelativePath(tests, path), Parts: parts)))
            .ToList();
        Assert.NotEmpty(built);

        var failures = built
            .Where(path => !Exists(path.Parts))
            .Select(path => $"{path.File} builds {string.Join('/', path.Parts)}, which does not exist")
            .Distinct()
            .ToList();

        Assert.True(failures.Count == 0, "A test reads a path that is not there, so it can pass with nothing checked:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ThePathRule_CatchesAFolderThatMoved()
    {
        var parts = Assert.Single(SolutionPathsIn("var root = Path.Combine(TestPaths.RepoRoot, \"src\", \"StarterApp.Api\", \"Application\");"));

        Assert.False(Exists(parts));
    }

    internal static List<string> UnknownGrantees(IEnumerable<(string From, string To)> granted, IReadOnlySet<string> projects) =>
        granted.Where(grant => !projects.Contains(grant.To)).Select(grant => $"{grant.From} shows its internals to {grant.To}").ToList();

    private static HashSet<string> SolutionProjects() =>
        XDocument.Load(Path.Combine(TestPaths.RepoRoot, $"{AppPrefix}.slnx"))
            .Descendants("Project")
            .Select(project => Path.GetFileNameWithoutExtension((string)project.Attribute("Path")!))
            .ToHashSet(StringComparer.Ordinal);

    // A Path.Combine of literals only (after an optional root) whose first two parts name a folder of the solution, such as "src", "<App>.Functions".
    internal static IEnumerable<string[]> SolutionPathsIn(string source) =>
        LiteralPathCombine().Matches(source)
            .Select(match => match.Groups["part"].Captures.Select(capture => capture.Value).ToArray())
            .Where(parts => parts.Length >= 2 && parts[0] is "src" or "dev" or "tests" && parts[1].StartsWith(AppPrefix + ".", StringComparison.Ordinal));

    private static bool Exists(string[] parts)
    {
        var path = Path.Combine([TestPaths.RepoRoot, .. parts]);
        return File.Exists(path) || Directory.Exists(path);
    }

    [GeneratedRegex("""Path\.Combine\(\s*(?:[\w.]+\s*,\s*)?"(?<part>[^"{}]+)"(?:\s*,\s*"(?<part>[^"{}]+)")*\s*\)""")]
    private static partial Regex LiteralPathCombine();
}
