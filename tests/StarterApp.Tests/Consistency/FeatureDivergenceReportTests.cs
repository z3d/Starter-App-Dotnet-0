namespace StarterApp.Tests.Consistency;

public class FeatureDivergenceReportTests
{
    [Fact]
    public void BooleanFeature_DetectsDivergenceFromExemplarMode()
    {
        var exemplar1 = HandlerFingerprintBuilder.A().Named("E1").WithLogger().Build();
        var exemplar2 = HandlerFingerprintBuilder.A().Named("E2").WithLogger().Build();
        var conforming = HandlerFingerprintBuilder.A().Named("Good").WithLogger().Build();
        var divergent = HandlerFingerprintBuilder.A().Named("Bad").WithLogger(false).Build();

        ICohortFingerprint[] all = [conforming, divergent];
        ICohortFingerprint[] exemplars = [exemplar1, exemplar2];

        var report = FeatureDivergenceReport.Analyse(all, exemplars);
        var loggerFeature = report.First(r => r.FeatureName == "HasLogger");

        Assert.True(loggerFeature.HasDivergence);
        Assert.Single(loggerFeature.DivergentMembers);
        Assert.Equal("Bad", loggerFeature.DivergentMembers[0].TypeName);
    }

    [Fact]
    public void BooleanFeature_NoDivergenceWhenAllMatch()
    {
        var exemplar = HandlerFingerprintBuilder.A().Named("E1").WithLogger().Build();
        var member = HandlerFingerprintBuilder.A().Named("M1").WithLogger().Build();

        ICohortFingerprint[] all = [member];
        ICohortFingerprint[] exemplars = [exemplar];

        var report = FeatureDivergenceReport.Analyse(all, exemplars);
        var loggerFeature = report.First(r => r.FeatureName == "HasLogger");

        Assert.False(loggerFeature.HasDivergence);
    }

    [Fact]
    public void NumericFeature_DetectsOutliersAboveThreshold()
    {
        var exemplar1 = HandlerFingerprintBuilder.A().Named("E1").WithIlByteSize(80).Build();
        var exemplar2 = HandlerFingerprintBuilder.A().Named("E2").WithIlByteSize(90).Build();
        var normal = HandlerFingerprintBuilder.A().Named("Normal").WithIlByteSize(85).Build();
        var outlier = HandlerFingerprintBuilder.A().Named("Big").WithIlByteSize(400).Build();

        ICohortFingerprint[] all = [normal, outlier];
        ICohortFingerprint[] exemplars = [exemplar1, exemplar2];

        var report = FeatureDivergenceReport.Analyse(all, exemplars);
        var lineFeature = report.First(r => r.FeatureName == "IlByteSize");

        Assert.True(lineFeature.HasDivergence);
        Assert.Contains(lineFeature.DivergentMembers, m => m.TypeName == "Big");
        Assert.DoesNotContain(lineFeature.DivergentMembers, m => m.TypeName == "Normal");
    }

    [Fact]
    public void NumericFeature_AllZeroExemplars_FlagsFirstNonZeroCount()
    {
        var exemplar1 = HandlerFingerprintBuilder.A().Named("E1").WithPrivateMethods(0).Build();
        var exemplar2 = HandlerFingerprintBuilder.A().Named("E2").WithPrivateMethods(0).Build();
        var member = HandlerFingerprintBuilder.A().Named("M1").WithPrivateMethods(1).Build();

        ICohortFingerprint[] all = [member];
        ICohortFingerprint[] exemplars = [exemplar1, exemplar2];

        var report = FeatureDivergenceReport.Analyse(all, exemplars);
        var privateMethodFeature = report.First(r => r.FeatureName == "PrivateMethodCount");

        Assert.True(privateMethodFeature.HasDivergence);
        Assert.Contains(privateMethodFeature.DivergentMembers, m => m.TypeName == "M1");
    }

    [Fact]
    public void ReturnsOneEntryPerFeature()
    {
        var exemplar = HandlerFingerprintBuilder.A().Named("E1").Build();
        var member = HandlerFingerprintBuilder.A().Named("M1").Build();

        ICohortFingerprint[] all = [member];
        ICohortFingerprint[] exemplars = [exemplar];

        var report = FeatureDivergenceReport.Analyse(all, exemplars);
        Assert.Equal(exemplar.FeatureNames.Length, report.Count);
    }

    [Fact]
    public void ThrowsOnEmptyExemplars()
    {
        Assert.Throws<ArgumentException>(() =>
            FeatureDivergenceReport.Analyse(
                [HandlerFingerprintBuilder.A().Named("M1").Build()],
                Array.Empty<ICohortFingerprint>()));
    }

    [Fact]
    public void NumericCountFeature_NotMisclassifiedAsBoolean_WhenExemplarValuesAreZeroOrOne()
    {
        // PrivateMethodCount and EntityLoadCount are numeric counts that happen to be
        // 0 or 1 in the exemplar set. The old code inferred boolean from values; the
        // fix uses explicit FeatureKinds metadata. This test would have caught the bug:
        // a member with PrivateMethodCount=3 should be treated as a numeric outlier,
        // not as "divergent from boolean mode 1.0".
        var exemplar1 = HandlerFingerprintBuilder.A().Named("E1").WithPrivateMethods(0).WithEntityLoads(1).Build();
        var exemplar2 = HandlerFingerprintBuilder.A().Named("E2").WithPrivateMethods(1).WithEntityLoads(1).Build();
        var member = HandlerFingerprintBuilder.A().Named("M1").WithPrivateMethods(3).WithEntityLoads(5).Build();

        ICohortFingerprint[] all = [member];
        ICohortFingerprint[] exemplars = [exemplar1, exemplar2];

        var report = FeatureDivergenceReport.Analyse(all, exemplars);

        var privateMethodFeature = report.First(r => r.FeatureName == "PrivateMethodCount");
        var entityLoadFeature = report.First(r => r.FeatureName == "EntityLoadCount");

        // These must be classified as numeric, not boolean
        Assert.False(privateMethodFeature.IsBoolean,
            "PrivateMethodCount should be classified as numeric even when exemplar values are 0 and 1");
        Assert.False(entityLoadFeature.IsBoolean,
            "EntityLoadCount should be classified as numeric even when exemplar values are all 1");
    }

    [Fact]
    public void RealCohort_HasTryCatchDivergenceMatchesActualTryCatchHandlers()
    {
        // Property-based assertion (replaces the earlier hardcoded two-handler list):
        // every handler with HasTryCatch=true should appear in the HasTryCatch divergent list,
        // every handler without should not. The set of SERIALIZABLE handlers can grow or
        // shrink without requiring this test to be edited — the invariant is "divergent
        // membership matches the feature value across the cohort".
        var cohort = new CommandHandlerCohort();
        var fingerprints = cohort.DiscoverTypes().Select(cohort.Extract).ToList();
        var exemplars = fingerprints.Where(f => cohort.ExemplarTypeNames.Contains(f.TypeName)).ToList();

        ICohortFingerprint[] all = fingerprints.ToArray<ICohortFingerprint>();
        ICohortFingerprint[] ex = exemplars.ToArray<ICohortFingerprint>();

        var report = FeatureDivergenceReport.Analyse(all, ex);
        Assert.NotEmpty(report);

        var tryCatchFeature = report.First(r => r.FeatureName == "HasTryCatch");

        // Exemplars don't use try/catch (see README), so divergent members = all handlers that do.
        var expectedDivergent = fingerprints
            .Where(f => f.HasTryCatch)
            .Select(f => f.TypeName)
            .ToHashSet();
        var actualDivergent = tryCatchFeature.DivergentMembers.Select(m => m.TypeName).ToHashSet();

        Assert.Equal(expectedDivergent, actualDivergent);
    }

    [Fact]
    public void RealQueryCohort_SubShapeBooleansDoNotReportPinnedShapesAsDivergent()
    {
        // Query handlers have three pinned sub-shapes: by-id, unpaginated list, and
        // paginated list. A boolean value should not be called divergent merely because
        // it is not the exemplar majority; it should be compared to the nearest exemplar.
        var cohort = new QueryHandlerCohort();
        var fingerprints = cohort.DiscoverTypes().Select(cohort.Extract).ToList();
        var exemplars = fingerprints.Where(f => cohort.ExemplarTypeNames.Contains(f.TypeName)).ToList();

        ICohortFingerprint[] all = fingerprints.ToArray<ICohortFingerprint>();
        ICohortFingerprint[] ex = exemplars.ToArray<ICohortFingerprint>();

        var report = FeatureDivergenceReport.Analyse(all, ex);

        Assert.Empty(report.First(r => r.FeatureName == "HasPagination").DivergentMembers);
        Assert.Empty(report.First(r => r.FeatureName == "IsCacheable").DivergentMembers);
        Assert.Empty(report.First(r => r.FeatureName == "ReturnsList").DivergentMembers);
    }
}
