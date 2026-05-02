namespace StarterApp.Tests.Consistency;

public class ConsistencyScorerTests
{
    [Fact]
    public void ComputeCentroid_SingleExemplar_ReturnsSameVector()
    {
        var exemplar = HandlerFingerprintBuilder.A().Named("Handler1").WithIlByteSize(30).WithDeps(2).WithLogger().Build();
        ICohortFingerprint[] exemplars = [exemplar];
        var centroid = ConsistencyScorer.ComputeCentroid(exemplars);

        Assert.Equal(exemplar.ToVector(), centroid);
    }

    [Fact]
    public void ComputeCentroid_MultipleExemplars_ReturnsAverage()
    {
        var e1 = HandlerFingerprintBuilder.A().Named("H1").WithIlByteSize(20).WithDeps(1).Build();
        var e2 = HandlerFingerprintBuilder.A().Named("H2").WithIlByteSize(40).WithDeps(3).Build();
        ICohortFingerprint[] exemplars = [e1, e2];
        var centroid = ConsistencyScorer.ComputeCentroid(exemplars);

        Assert.Equal(30.0, centroid[0]);
        Assert.Equal(2.0, centroid[1]);
    }

    [Fact]
    public void ComputeDistance_IdenticalToExemplar_ReturnsZero()
    {
        var exemplar = HandlerFingerprintBuilder.A().Named("H1").WithIlByteSize(30).WithDeps(2).WithLogger().Build();
        ICohortFingerprint[] exemplars = [exemplar];
        var centroid = ConsistencyScorer.ComputeCentroid(exemplars);
        var inverseCov = ConsistencyScorer.ComputeInverseCovariance(exemplars, centroid);
        var distance = ConsistencyScorer.ComputeDistance(exemplar, centroid, inverseCov);

        Assert.Equal(0.0, distance, precision: 10);
    }

    [Fact]
    public void ComputeDistance_DifferentHandler_ReturnsPositive()
    {
        var exemplar = HandlerFingerprintBuilder.A().Named("H1").WithIlByteSize(30).WithDeps(2).WithLogger().Build();
        var outlier = HandlerFingerprintBuilder.A().Named("H2").WithIlByteSize(150).WithDeps(1).WithLogger(false).WithTryCatch().Build();

        ICohortFingerprint[] exemplars = [exemplar];
        var centroid = ConsistencyScorer.ComputeCentroid(exemplars);
        var inverseCov = ConsistencyScorer.ComputeInverseCovariance(exemplars, centroid);
        var distance = ConsistencyScorer.ComputeDistance(outlier, centroid, inverseCov);

        Assert.True(distance > 0);
    }

    [Fact]
    public void ScoreAll_OutlierRanksHigherThanSimilar()
    {
        var exemplar = HandlerFingerprintBuilder.A().Named("Exemplar").WithIlByteSize(30).WithDeps(2).WithLogger().Build();
        var similar = HandlerFingerprintBuilder.A().Named("Similar").WithIlByteSize(35).WithDeps(2).WithLogger().Build();
        var outlier = HandlerFingerprintBuilder.A().Named("Outlier").WithIlByteSize(150).WithDeps(1).WithLogger(false).WithTryCatch().Build();

        ICohortFingerprint[] all = [exemplar, similar, outlier];
        ICohortFingerprint[] exemplars = [exemplar];
        var scores = ConsistencyScorer.ScoreAll(all, exemplars);

        Assert.Equal("Outlier", scores[0].TypeName);
        Assert.True(scores[0].Distance > scores[1].Distance);
    }

    [Fact]
    public void FeatureContributions_IdentifiesTopContributor()
    {
        var exemplar = HandlerFingerprintBuilder.A().Named("Exemplar").WithIlByteSize(30).WithDeps(2).Build();
        var outlier = HandlerFingerprintBuilder.A().Named("Outlier").WithIlByteSize(150).WithDeps(2).Build();

        ICohortFingerprint[] all = [outlier];
        ICohortFingerprint[] exemplars = [exemplar];
        var scores = ConsistencyScorer.ScoreAll(all, exemplars);

        Assert.Equal("IlByteSize", scores[0].TopContributor);
    }

    [Fact]
    public void ComputeCentroid_ThrowsOnEmptyExemplars()
    {
        Assert.Throws<ArgumentException>(() =>
            ConsistencyScorer.ComputeCentroid(Array.Empty<ICohortFingerprint>()));
    }

    [Fact]
    public void Mahalanobis_DistinguishesCorrelatedFromAntiCorrelated()
    {
        // Two features perfectly correlated in exemplars: lineCount and deps move together.
        // Mahalanobis should give a LARGER distance to anti-correlated deviations than
        // to correlated ones, because anti-correlated is "more unusual" given the exemplar
        // pattern. This is the key property that standardised Euclidean lacks.
        var e1 = HandlerFingerprintBuilder.A().Named("E1").WithIlByteSize(30).WithDeps(1).Build();
        var e2 = HandlerFingerprintBuilder.A().Named("E2").WithIlByteSize(60).WithDeps(2).Build();
        var e3 = HandlerFingerprintBuilder.A().Named("E3").WithIlByteSize(90).WithDeps(3).Build();

        ICohortFingerprint[] exemplars = [e1, e2, e3];
        var centroid = ConsistencyScorer.ComputeCentroid(exemplars);
        var inverseCov = ConsistencyScorer.ComputeInverseCovariance(exemplars, centroid);

        // Correlated direction: both features deviate the same way as the exemplar pattern
        var correlated = HandlerFingerprintBuilder.A().Named("Correlated").WithIlByteSize(120).WithDeps(4).Build();
        // Anti-correlated: lineCount high but deps low (breaks the pattern)
        var antiCorrelated = HandlerFingerprintBuilder.A().Named("AntiCorrelated").WithIlByteSize(120).WithDeps(0).Build();

        var dCorr = ConsistencyScorer.ComputeDistance(correlated, centroid, inverseCov);
        var dAnti = ConsistencyScorer.ComputeDistance(antiCorrelated, centroid, inverseCov);

        // Anti-correlated should score higher (more unusual) than correlated
        Assert.True(dAnti > dCorr,
            $"Anti-correlated distance ({dAnti:F3}) should exceed correlated ({dCorr:F3}). " +
            "Mahalanobis penalises deviations that break the exemplar correlation structure.");
    }

    [Fact]
    public void Mahalanobis_WithSingleExemplar_DegradesToScaledDistance()
    {
        // With 1 exemplar, the sample covariance is all zeros, shrinkage intensity = 1.0,
        // so the result should be purely scaled-identity distance (similar to standardised
        // Euclidean but with a uniform variance scale).
        var exemplar = HandlerFingerprintBuilder.A().Named("E1").WithIlByteSize(50).WithDeps(2).WithLogger().Build();
        var candidate = HandlerFingerprintBuilder.A().Named("C1").WithIlByteSize(100).WithDeps(4).Build();

        ICohortFingerprint[] exemplars = [exemplar];
        var centroid = ConsistencyScorer.ComputeCentroid(exemplars);
        var inverseCov = ConsistencyScorer.ComputeInverseCovariance(exemplars, centroid);
        var distance = ConsistencyScorer.ComputeDistance(candidate, centroid, inverseCov);

        // Should produce a finite positive distance (not NaN, not infinity)
        Assert.True(distance > 0 && double.IsFinite(distance),
            $"Distance with single exemplar should be finite positive, got {distance}");
    }
}
