namespace StarterApp.Tests.Consistency;

/// <summary>
/// Per-feature divergence analysis. Complements the composite Mahalanobis distance
/// by surfacing which specific features are drifting across the cohort.
/// The composite score answers "which members are overall outliers?"
/// This report answers "which specific features are drifting, and in whom?"
/// </summary>
public static class FeatureDivergenceReport
{
    private const double Epsilon = 1e-10;

    public static IReadOnlyList<FeatureDivergence> Analyse(
        IReadOnlyList<ICohortFingerprint> allMembers,
        IReadOnlyList<ICohortFingerprint> exemplars,
        double numericSigmaThreshold = 2.0)
    {
        if (exemplars.Count == 0)
            throw new ArgumentException("At least one exemplar is required.", nameof(exemplars));

        var featureNames = exemplars[0].FeatureNames;
        var featureKinds = exemplars[0].FeatureKinds;
        var dim = featureNames.Length;
        var results = new List<FeatureDivergence>();

        var exemplarVectors = exemplars.Select(e => e.ToVector()).ToList();
        var featureStats = BuildFeatureStats(exemplarVectors, featureKinds, dim);
        var membersWithNearestExemplar = allMembers
            .Select(member => new MemberReference(
                member,
                member.ToVector(),
                FindNearestExemplarVector(member.ToVector(), exemplarVectors, featureStats)))
            .ToList();

        for (var i = 0; i < dim; i++)
        {
            var isBoolean = featureKinds[i] == FeatureKind.Boolean;

            List<DivergentMember> divergent;

            if (isBoolean)
            {
                divergent = membersWithNearestExemplar
                    .Where(x => Math.Abs(x.Vector[i] - x.NearestExemplarVector[i]) > 0.5)
                    .Select(x => new DivergentMember(x.Member.TypeName, x.Vector[i], x.NearestExemplarVector[i]))
                    .ToList();

                results.Add(new FeatureDivergence(
                    featureNames[i],
                    isBoolean,
                    featureStats[i].Mean,
                    featureStats[i].StdDev,
                    divergent));
            }
            else
            {
                var mean = featureStats[i].Mean;
                var stdDev = featureStats[i].StdDev;
                var effectiveThreshold = NumericDivergenceThreshold(mean, stdDev, numericSigmaThreshold);

                divergent = membersWithNearestExemplar
                    .Where(x => Math.Abs(x.Vector[i] - mean) > effectiveThreshold)
                    .Select(x => new DivergentMember(x.Member.TypeName, x.Vector[i], mean))
                    .OrderByDescending(x => Math.Abs(x.ActualValue - x.ExemplarValue))
                    .ToList();

                results.Add(new FeatureDivergence(
                    featureNames[i],
                    isBoolean,
                    mean,
                    stdDev,
                    divergent));
            }
        }

        return results;
    }

    private static FeatureStats[] BuildFeatureStats(
        IReadOnlyList<double[]> exemplarVectors,
        FeatureKind[] featureKinds,
        int dim)
    {
        var stats = new FeatureStats[dim];

        for (var i = 0; i < dim; i++)
        {
            var values = exemplarVectors.Select(v => v[i]).ToList();
            var mean = values.Average();
            var variance = values.Count > 1
                ? values.Sum(v => (v - mean) * (v - mean)) / values.Count
                : 0.0;
            var stdDev = Math.Sqrt(variance);
            var scale = featureKinds[i] == FeatureKind.Boolean
                ? 1.0
                : NumericScale(mean, stdDev);

            stats[i] = new FeatureStats(mean, stdDev, scale);
        }

        return stats;
    }

    private static double[] FindNearestExemplarVector(
        double[] memberVector,
        IReadOnlyList<double[]> exemplarVectors,
        FeatureStats[] featureStats)
    {
        var bestDistance = double.PositiveInfinity;
        var bestVector = exemplarVectors[0];

        foreach (var exemplarVector in exemplarVectors)
        {
            var distance = 0.0;
            for (var i = 0; i < memberVector.Length; i++)
            {
                var scale = featureStats[i].Scale;
                var diff = scale > Epsilon
                    ? (memberVector[i] - exemplarVector[i]) / scale
                    : memberVector[i] - exemplarVector[i];
                distance += diff * diff;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestVector = exemplarVector;
            }
        }

        return bestVector;
    }

    private static double NumericScale(double mean, double stdDev) =>
        stdDev > Epsilon ? stdDev : Math.Max(Math.Abs(mean) * 0.1, 1.0);

    private static double NumericDivergenceThreshold(double mean, double stdDev, double numericSigmaThreshold)
    {
        if (stdDev > Epsilon)
            return stdDev * numericSigmaThreshold;

        // Rare count detector: when every exemplar is zero, the first non-zero value is
        // meaningful drift. A fallback threshold of 1 or 2 would hide exactly that signal.
        if (Math.Abs(mean) < Epsilon)
            return 0.0;

        return NumericScale(mean, stdDev) * numericSigmaThreshold;
    }

    private sealed record FeatureStats(double Mean, double StdDev, double Scale);

    private sealed record MemberReference(
        ICohortFingerprint Member,
        double[] Vector,
        double[] NearestExemplarVector);
}

public record FeatureDivergence(
    string FeatureName,
    bool IsBoolean,
    double ExemplarValue,
    double ExemplarStdDev,
    IReadOnlyList<DivergentMember> DivergentMembers)
{
    public int DivergentCount => DivergentMembers.Count;
    public bool HasDivergence => DivergentMembers.Count > 0;
}

public record DivergentMember(
    string TypeName,
    double ActualValue,
    double ExemplarValue);
