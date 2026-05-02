using System.Text.RegularExpressions;

namespace StarterApp.Tests.Consistency;

/// <summary>
/// Shared validation properties that apply to every cohort: cohort discovery matches
/// the filesystem, fingerprint extraction produces non-degenerate values, and the
/// exemplar set named in code matches the exemplar set named in the docs README.
/// </summary>
/// <remarks>
/// Cohort-specific assertions (known outliers, feature-threshold checks, anti-pattern
/// detection like "no list query is cacheable") stay in the derived class — those
/// don't generalise because each cohort has its own invariants.
/// </remarks>
public abstract class CohortValidationTestBase<TFingerprint>
    where TFingerprint : ICohortFingerprint
{
    protected abstract ICohortDefinition<TFingerprint> Cohort { get; }
    protected abstract string ExemplarDocsFolder { get; }
    protected abstract string SourceTreeRelativePath { get; }
    protected abstract string SourceFileGlob { get; }

    protected virtual string SourceFileNameToTypeName(string fileNameWithoutExtension) =>
        fileNameWithoutExtension;

    /// <summary>
    /// Regex that matches exemplar-declaration lines in the README. Default matches the
    /// <c>**`FooCommandHandler.cs`**</c> convention used by every README in this project.
    /// Override if a cohort's README uses a different format.
    /// </summary>
    protected virtual string ReadmeExemplarPattern =>
        @"\*\*`(\w+" + ExemplarNameSuffix + @")\.cs`\*\*";

    /// <summary>
    /// Type-name suffix that cohort members share, e.g. <c>CommandHandler</c>, used to
    /// parse exemplar names out of the README.
    /// </summary>
    protected abstract string ExemplarNameSuffix { get; }

    /// <summary>
    /// Cohort-specific non-degeneracy check for a single fingerprint. Typically asserts
    /// that key numeric features are positive and required booleans are set; the exact
    /// rules differ per cohort.
    /// </summary>
    protected abstract void AssertFingerprintIsValid(TFingerprint fingerprint);

    protected static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "docs")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException(
            "Could not find repo root (directory containing docs/) by walking up from " + AppContext.BaseDirectory);
    }

    protected string ExemplarReadmePath =>
        Path.Combine(FindRepoRoot(), "docs", "exemplars", ExemplarDocsFolder, "README.md");

    protected string SourceTreeAbsolutePath =>
        Path.Combine(FindRepoRoot(), SourceTreeRelativePath);

    [Fact]
    public void CohortDiscovery_FindsEveryFileOnDisk()
    {
        var discovered = Cohort.DiscoverTypes().Select(t => t.Name).ToHashSet();

        var onDisk = Directory
            .EnumerateFiles(SourceTreeAbsolutePath, SourceFileGlob, SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => SourceFileNameToTypeName(name!))
            .ToHashSet();

        var missingFromDiscovery = onDisk.Except(discovered).ToList();
        var extraInDiscovery = discovered.Except(onDisk).ToList();

        Assert.True(
            missingFromDiscovery.Count == 0 && extraInDiscovery.Count == 0,
            $"{Cohort.CohortName} discovery drifted from filesystem:\n" +
            $"  On disk but not discovered: [{string.Join(", ", missingFromDiscovery)}]\n" +
            $"  Discovered but not on disk: [{string.Join(", ", extraInDiscovery)}]");
    }

    [Fact]
    public void CohortExtraction_ProducesValidFingerprints()
    {
        var fingerprints = Cohort.DiscoverTypes().Select(Cohort.Extract).ToList();
        Assert.NotEmpty(fingerprints);

        foreach (var fp in fingerprints)
            AssertFingerprintIsValid(fp);
    }

    [Fact]
    public void ExemplarAlignment_CodeMatchesDocs()
    {
        var readmeLines = File.ReadAllLines(ExemplarReadmePath)
            .Where(line => !line.TrimStart().StartsWith("<!--"));
        var readmeText = string.Join("\n", readmeLines);
        var documented = Regex.Matches(readmeText, ReadmeExemplarPattern)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        Assert.True(documented.Count > 0,
            $"Could not parse any exemplar names from {ExemplarReadmePath} using pattern {ReadmeExemplarPattern}.");

        var coded = Cohort.ExemplarTypeNames.ToHashSet();

        var inDocsNotCode = documented.Except(coded).ToList();
        var inCodeNotDocs = coded.Except(documented).ToList();

        Assert.True(inDocsNotCode.Count == 0 && inCodeNotDocs.Count == 0,
            $"Exemplar set mismatch between code and {ExemplarReadmePath}.\n" +
            $"In docs but not code: [{string.Join(", ", inDocsNotCode)}]\n" +
            $"In code but not docs: [{string.Join(", ", inCodeNotDocs)}]");
    }
}
