namespace StarterApp.Tests.Consistency;

public sealed record CohortConsistencyReport(
    string CohortName,
    IReadOnlyList<Type> MemberTypes,
    IReadOnlyList<ICohortFingerprint> Fingerprints,
    IReadOnlyList<ICohortFingerprint> Exemplars,
    IReadOnlyList<CohortScore> StructuralScores,
    IReadOnlyList<ShingleScore> ShingleScores,
    IReadOnlyList<EmbeddingScore> EmbeddingScores,
    IReadOnlyList<FeatureDivergence> FeatureDivergences);

public static class CohortConsistencyReporter
{
    public static CohortConsistencyReport Build<TFingerprint>(
        ICohortDefinition<TFingerprint> cohort,
        ICodeEmbedder? embedder = null)
        where TFingerprint : ICohortFingerprint
    {
        var memberTypes = cohort.DiscoverTypes();
        var fingerprints = memberTypes.Select(cohort.Extract).Cast<ICohortFingerprint>().ToList();

        var exemplarNames = cohort.ExemplarTypeNames.ToHashSet();
        var exemplars = fingerprints.Where(f => exemplarNames.Contains(f.TypeName)).ToList();
        var exemplarTypes = memberTypes.Where(t => exemplarNames.Contains(t.Name)).ToList();

        if (memberTypes.Count == 0)
            throw new InvalidOperationException($"{cohort.CohortName} has no discovered members.");

        if (exemplars.Count == 0 || exemplarTypes.Count == 0)
            throw new InvalidOperationException($"{cohort.CohortName} has no resolved exemplars.");

        if (exemplars.Count != cohort.ExemplarTypeNames.Count || exemplarTypes.Count != cohort.ExemplarTypeNames.Count)
        {
            var resolvedNames = exemplars.Select(e => e.TypeName).ToHashSet();
            var missing = cohort.ExemplarTypeNames.Except(resolvedNames).ToList();
            throw new InvalidOperationException(
                $"{cohort.CohortName} exemplar resolution mismatch. Missing: [{string.Join(", ", missing)}]");
        }

        var all = fingerprints.ToArray();
        var ex = exemplars.ToArray();
        var codeEmbedder = embedder ?? new SourceTokenEmbedder();

        return new CohortConsistencyReport(
            cohort.CohortName,
            memberTypes,
            fingerprints,
            exemplars,
            ConsistencyScorer.ScoreAll(all, ex),
            AstShingleComparer.ScoreAll(memberTypes, exemplarTypes),
            EmbeddingSimilarityScorer.ScoreAll(memberTypes, exemplarTypes, codeEmbedder),
            FeatureDivergenceReport.Analyse(all, ex));
    }
}
