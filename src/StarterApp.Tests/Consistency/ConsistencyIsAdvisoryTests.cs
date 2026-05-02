namespace StarterApp.Tests.Consistency;

/// <summary>
/// Meta-test: asserts the consistency suite never gates behaviour on a distance
/// threshold. From the paper:
///
///   "Do not gate merges on consistency distance. Consistency-by-distance has a
///    known pathology: it punishes the first correct instance of a new pattern
///    and rewards the tenth copy of a bad one."
///
/// This test scans every method in every test class in <c>Consistency/</c> and
/// asserts that if it reads a <see cref="CohortScore.Distance"/> value, it does so
/// only to log, report, or rank — never inside an <c>Assert.True(distance &lt; X)</c>
/// style predicate that would fail a build when a score crosses an arbitrary
/// numerical line.
///
/// The check is conservative: any test method that both reads <c>Distance</c> and
/// passes it (or a value derived from it) to <c>Assert.True</c>/<c>InRange</c>
/// with a literal double must live on an allow-list of methods that have been
/// manually reviewed. That allow-list is small and its entries compute a
/// sigma-relative or rank-relative threshold from the data itself — not a
/// hardcoded "distance must be less than 5.0" constant.
/// </summary>
public class ConsistencyIsAdvisoryTests
{
    private static readonly Assembly TestAssembly = typeof(ConsistencyIsAdvisoryTests).Assembly;

    /// <summary>
    /// Tests that legitimately assert on distance in a data-relative way (e.g. "outlier
    /// distance exceeds mean + 1 sigma, computed from the current scores"). Adding a
    /// method to this list is a deliberate act and should be justified in review.
    /// </summary>
    private static readonly HashSet<string> DataRelativeAssertionAllowlist =
    [
        // ConsistencyScorerTests — unit tests on the scorer itself. They assert
        // distance > 0 or distance > other-distance, never distance < literal-threshold.
        "ConsistencyScorerTests.ComputeDistance_IdenticalToExemplar_ReturnsZero",
        "ConsistencyScorerTests.ComputeDistance_DifferentHandler_ReturnsPositive",
        "ConsistencyScorerTests.ScoreAll_OutlierRanksHigherThanSimilar",
        "ConsistencyScorerTests.Mahalanobis_DistinguishesCorrelatedFromAntiCorrelated",
        "ConsistencyScorerTests.Mahalanobis_WithSingleExemplar_DegradesToScaledDistance",

        // CommandHandlerValidationTests — assertions on distance are computed from the
        // run's own scores (mean + 1 sigma), not from a baked-in numeric threshold.
        "CommandHandlerValidationTests.StructuralFingerprint_KnownOutlierExceedsOneSigma",
        "CommandHandlerValidationTests.CombinedScoring_ProducesRankedOutput",

        // Governance tests for every cohort — assert exemplar centroids differ from
        // the full-cohort centroid with a 1e-6 epsilon. Not a gating distance threshold.
        "CommandHandlerGovernanceTests.CentroidIsAnchoredToExemplarsOnly_NotTheFullCohort",
        "QueryHandlerGovernanceTests.CentroidIsAnchoredToExemplarsOnly_NotTheFullCohort",
        "EfConfigurationGovernanceTests.CentroidIsAnchoredToExemplarsOnly_NotTheFullCohort",

        // Cohort-specific validation tests that compare a known outlier against a
        // mean+1σ threshold computed from the current cohort's own scores.
    ];

    [Fact]
    public void NoConsistencyTestGatesOnALiteralDistanceThreshold()
    {
        // Find every test class under the Consistency namespace.
        var consistencyTestTypes = TestAssembly.GetTypes()
            .Where(t => t.Namespace == "StarterApp.Tests.Consistency"
                && t.IsClass
                && !t.IsAbstract
                && t.Name.EndsWith("Tests", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(consistencyTestTypes);

        var violations = new List<string>();

        foreach (var type in consistencyTestTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsTestMethod(method))
                    continue;

                var qualifiedName = $"{type.Name}.{method.Name}";
                if (DataRelativeAssertionAllowlist.Contains(qualifiedName))
                    continue;

                if (MethodBothReadsDistanceAndAssertsWithLiteralDouble(method))
                    violations.Add(qualifiedName);
            }
        }

        Assert.True(violations.Count == 0,
            "Consistency tests must remain advisory. The following tests appear to assert a hardcoded " +
            "distance threshold, which would gate builds on an arbitrary numeric line and punish the " +
            "first correct instance of a new pattern. Use a data-relative assertion (sigma, rank, or " +
            "comparison between scores) or add an allow-list entry with a review justification.\n" +
            string.Join("\n", violations));
    }

    private static bool IsTestMethod(MethodInfo method) =>
        method.GetCustomAttributes().Any(a =>
            a.GetType().Name is "FactAttribute" or "TheoryAttribute");

    /// <summary>
    /// Opens the method's metadata and walks its IL. Returns true if the method
    /// both (a) reads the <c>Distance</c> getter on <see cref="CohortScore"/> and
    /// (b) loads a <c>double</c> literal that could be used as a comparison
    /// threshold. That is an over-approximation — it flags any method that scores
    /// distances and also references a literal — so the allow-list covers the
    /// legitimate cases where the literal is a sigma multiplier, a precision, etc.
    /// </summary>
    private static bool MethodBothReadsDistanceAndAssertsWithLiteralDouble(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body is null)
            return false;
        var il = body.GetILAsByteArray();
        if (il is null)
            return false;

        var module = method.Module;
        var readsDistance = false;
        var loadsDoubleLiteral = false;

        for (var i = 0; i < il.Length; i++)
        {
            var op = il[i];
            switch (op)
            {
                case 0x28 or 0x6F: // call / callvirt
                    {
                        var token = BitConverter.ToInt32(il, i + 1);
                        i += 4;
                        try
                        {
                            var target = module.ResolveMethod(token);
                            if (target is { Name: "get_Distance", DeclaringType: { Name: "CohortScore" } })
                                readsDistance = true;
                        }
                        catch
                        {
                            // Unresolvable tokens (cross-module generics) are irrelevant here.
                        }
                        break;
                    }
                case 0x23: // ldc.r8 — 8-byte double literal
                    loadsDoubleLiteral = true;
                    i += 8;
                    break;
                case 0x22: // ldc.r4 — 4-byte float literal (unlikely here but handle for safety)
                    loadsDoubleLiteral = true;
                    i += 4;
                    break;
                case 0x20: // ldc.i4
                    i += 4;
                    break;
                case 0x21 or 0x27 or 0x72 or 0x1F: // ldc.i8/calli/ldstr/ldc.i4.s
                    i += op == 0x1F ? 1 : (op == 0x21 ? 8 : 4);
                    break;
            }
        }

        return readsDistance && loadsDoubleLiteral;
    }
}
