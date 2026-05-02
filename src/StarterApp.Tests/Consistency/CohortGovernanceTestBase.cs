using System.Text.RegularExpressions;

namespace StarterApp.Tests.Consistency;

/// <summary>
/// Shared governance properties that apply to every cohort: exemplars reference real
/// source files, every exemplar has a written justification of a minimum prose length,
/// and the scorer genuinely anchors on the pinned exemplars rather than silently
/// collapsing to the full-cohort centroid.
/// </summary>
/// <remarks>
/// Extracted at the third cohort (EF configurations) because the first two copies made
/// the seam unambiguous: the only per-cohort differences are the cohort instance, the
/// docs subdirectory name, the source-tree directory to scan, and the filename suffix
/// that identifies cohort members on disk. Everything else is identical. Per the
/// paper's "rule of three", abstracting after two copies risks locking in the template
/// prematurely — a third cohort confirms what's shared.
/// </remarks>
public abstract class CohortGovernanceTestBase<TFingerprint>
    where TFingerprint : ICohortFingerprint
{
    protected abstract ICohortDefinition<TFingerprint> Cohort { get; }

    /// <summary>
    /// Folder under <c>docs/exemplars/</c> that holds this cohort's README. For
    /// command handlers this is <c>command-handlers</c>; for queries, <c>query-handlers</c>.
    /// </summary>
    protected abstract string ExemplarDocsFolder { get; }

    /// <summary>
    /// Path under the repo root where cohort members live on disk, e.g.
    /// <c>src/StarterApp.Api/Application</c> for handlers or
    /// <c>src/StarterApp.Api/Data/Configurations</c> for EF configurations.
    /// </summary>
    protected abstract string SourceTreeRelativePath { get; }

    /// <summary>
    /// File-glob suffix that identifies members on disk, e.g. <c>*CommandHandler.cs</c>.
    /// </summary>
    protected abstract string SourceFileGlob { get; }

    /// <summary>
    /// Converts a source file name into the cohort type name that file is expected to contain.
    /// StarterApp keeps each command/query record and its handler in a shared
    /// <c>FooCommand.cs</c>/<c>FooQuery.cs</c> file, so handler cohorts append
    /// <c>Handler</c> here.
    /// </summary>
    protected virtual string SourceFileNameToTypeName(string fileNameWithoutExtension) =>
        fileNameWithoutExtension;

    private static string FindRepoRoot()
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
    public void EveryExemplarInCode_ResolvesToAnExistingFile()
    {
        // If an exemplar is renamed or deleted without updating the cohort, the scorer
        // silently loses an anchor and the centroid shifts. Fail loudly so the deletion
        // requires an explicit governance decision.
        var onDisk = Directory
            .EnumerateFiles(SourceTreeAbsolutePath, SourceFileGlob, SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => SourceFileNameToTypeName(name!))
            .ToHashSet();

        var missing = Cohort.ExemplarTypeNames.Where(name => !onDisk.Contains(name)).ToList();

        Assert.True(missing.Count == 0,
            $"Exemplars referenced by {Cohort.GetType().Name} have no corresponding .cs file " +
            $"under {SourceTreeRelativePath}:\n  {string.Join("\n  ", missing)}\n" +
            $"Either restore the file, rename the exemplar, or update the cohort's ExemplarTypeNames.");
    }

    [Fact]
    public void EveryExemplar_HasAWrittenJustificationInReadme()
    {
        // The justification is the written record of why a file was considered canonical.
        // Without it, exemplar review degenerates into taste.
        var readme = File.ReadAllText(ExemplarReadmePath);

        var violations = new List<string>();
        foreach (var exemplarName in Cohort.ExemplarTypeNames)
        {
            var pattern = $@"\*\*`{Regex.Escape(exemplarName)}\.cs`\*\*\s*(?:—|-)\s*(.+?)(?:\r?\n\r?\n|\z)";
            var match = Regex.Match(readme, pattern, RegexOptions.Singleline);

            if (!match.Success)
            {
                violations.Add($"{exemplarName}: no justification line found after **`{exemplarName}.cs`**");
                continue;
            }

            var justification = match.Groups[1].Value.Trim();
            if (justification.Length < 40)
                violations.Add($"{exemplarName}: justification shorter than 40 characters (got {justification.Length}): \"{justification}\"");
        }

        Assert.True(violations.Count == 0,
            $"Exemplar justification gaps in {ExemplarReadmePath}:\n  {string.Join("\n  ", violations)}");
    }

    [Fact]
    public void CentroidIsAnchoredToExemplarsOnly_NotTheFullCohort()
    {
        // Load-bearing property of the whole paper: the centroid is computed over the
        // pinned exemplars, NOT the full cohort. If the scorer were silently wired to
        // the full cohort, the measure becomes self-confirming (drift gets rewarded for
        // resembling earlier drift).
        var all = Cohort.DiscoverTypes().Select(Cohort.Extract).ToList();
        var exemplars = all.Where(f => Cohort.ExemplarTypeNames.Contains(f.TypeName)).ToList();

        Assert.NotEmpty(exemplars);
        Assert.True(all.Count > exemplars.Count,
            "Test precondition: cohort must be larger than exemplar set for this property to be meaningful.");

        var exemplarCentroid = ConsistencyScorer.ComputeCentroid(exemplars.Cast<ICohortFingerprint>().ToArray());
        var fullCohortCentroid = ConsistencyScorer.ComputeCentroid(all.Cast<ICohortFingerprint>().ToArray());

        var differentOnSomeFeature = false;
        for (var i = 0; i < exemplarCentroid.Length; i++)
            if (Math.Abs(exemplarCentroid[i] - fullCohortCentroid[i]) > 1e-6)
                differentOnSomeFeature = true;

        Assert.True(differentOnSomeFeature,
            "Exemplar centroid equals full-cohort centroid on every feature — either the exemplar " +
            "set has drifted to be 'just another sample' or the feature vector is insensitive.");

        var candidate = all.First(f => !Cohort.ExemplarTypeNames.Contains(f.TypeName));

        var scoresAgainstExemplars = ConsistencyScorer.ScoreAll(
            new ICohortFingerprint[] { candidate },
            exemplars.Cast<ICohortFingerprint>().ToArray());
        var scoresAgainstFullCohort = ConsistencyScorer.ScoreAll(
            new ICohortFingerprint[] { candidate },
            all.Cast<ICohortFingerprint>().ToArray());

        Assert.True(
            Math.Abs(scoresAgainstExemplars[0].Distance - scoresAgainstFullCohort[0].Distance) > 1e-6,
            $"Scoring {candidate.TypeName} against the pinned exemplars ({scoresAgainstExemplars[0].Distance:F3}) " +
            $"produced the same distance as scoring against the full cohort ({scoresAgainstFullCohort[0].Distance:F3}). " +
            "This means the scorer is either ignoring its exemplars argument or the exemplar set is " +
            "statistically indistinguishable from the cohort — both break the paper's anchoring guarantee.");
    }
}
