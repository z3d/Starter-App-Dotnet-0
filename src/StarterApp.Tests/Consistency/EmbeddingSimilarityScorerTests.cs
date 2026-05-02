namespace StarterApp.Tests.Consistency;

public class EmbeddingSimilarityScorerTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        double[] a = [1.0, 2.0, 3.0];
        Assert.Equal(1.0, EmbeddingSimilarityScorer.CosineSimilarity(a, a), precision: 10);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        double[] a = [1.0, 0.0];
        double[] b = [0.0, 1.0];
        Assert.Equal(0.0, EmbeddingSimilarityScorer.CosineSimilarity(a, b), precision: 10);
    }

    [Fact]
    public void CosineSimilarity_DifferentDimensions_Throws()
    {
        double[] a = [1.0, 2.0];
        double[] b = [1.0, 2.0, 3.0];
        Assert.Throws<ArgumentException>(() => EmbeddingSimilarityScorer.CosineSimilarity(a, b));
    }

    [Fact]
    public void ComputeCentroid_AveragesExemplarEmbeddings()
    {
        double[] e1 = [2.0, 0.0, 4.0];
        double[] e2 = [0.0, 6.0, 0.0];
        var centroid = EmbeddingSimilarityScorer.ComputeCentroid([e1, e2]);

        Assert.Equal(1.0, centroid[0]);
        Assert.Equal(3.0, centroid[1]);
        Assert.Equal(2.0, centroid[2]);
    }

    [Fact]
    public void ComputeCentroid_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            EmbeddingSimilarityScorer.ComputeCentroid(Array.Empty<double[]>()));
    }

    [Fact]
    public void ScoreAll_SimilarCandidateScoresHigherThanDissimilar()
    {
        var embedder = new FixedEmbedder(new Dictionary<string, double[]>
        {
            ["ExemplarA"] = [1.0, 0.0, 0.0],
            ["ExemplarB"] = [0.9, 0.1, 0.0],
            ["Similar"] = [0.8, 0.2, 0.0],
            ["Dissimilar"] = [0.0, 0.0, 1.0],
        });

        var scores = EmbeddingSimilarityScorer.ScoreAll(
            allTypes: [typeof(SimilarStub), typeof(DissimilarStub)],
            exemplarTypes: [typeof(ExemplarAStub), typeof(ExemplarBStub)],
            embedder);

        var similar = scores.First(s => s.TypeName == nameof(SimilarStub));
        var dissimilar = scores.First(s => s.TypeName == nameof(DissimilarStub));

        Assert.True(similar.CosineSimilarity > dissimilar.CosineSimilarity,
            $"Similar ({similar.CosineSimilarity:F3}) should score higher than Dissimilar ({dissimilar.CosineSimilarity:F3})");
    }

    [Fact]
    public void SourceTokenEmbedder_ProducesConsistentDeterministicVectors()
    {
        var embedder = new SourceTokenEmbedder();
        var cohort = new CommandHandlerCohort();
        var handler = cohort.DiscoverTypes().First();

        var embedding1 = embedder.Embed(handler);
        var embedding2 = embedder.Embed(handler);

        Assert.Equal(embedding1, embedding2);
        Assert.Equal(embedder.Dimensions, embedding1.Length);
    }

    [Fact]
    public void SourceTokenEmbedder_DifferentDomainsProduceDifferentVectors()
    {
        var embedder = new SourceTokenEmbedder();
        var cohort = new CommandHandlerCohort();
        var types = cohort.DiscoverTypes();

        var productHandler = types.First(t => t.Name == "CreateProductCommandHandler");
        var orderHandler = types.First(t => t.Name == "CreateOrderCommandHandler");

        var similarity = EmbeddingSimilarityScorer.CosineSimilarity(
            embedder.Embed(productHandler),
            embedder.Embed(orderHandler));

        Assert.True(similarity < 0.99,
            $"Product and order handlers should not be near-identical (similarity={similarity:F3})");
    }

    [Fact]
    public void SourceTokenEmbedder_WalksInstructionBoundaries_NotByteByByte()
    {
        var types = new CommandHandlerCohort().DiscoverTypes();
        var createOrder = types.First(t => t.Name == "CreateOrderCommandHandler");
        var tokens = SourceTokenEmbedder.ExtractSemanticTokens(createOrder);

        var validPrefixes = new[] { "str:", "method:", "type:", "newtype:", "casttype:", "field:", "token:" };
        Assert.True(tokens.All(t => validPrefixes.Any(t.StartsWith)),
            $"Found token without valid prefix: {tokens.FirstOrDefault(t => !validPrefixes.Any(t.StartsWith))}");
        Assert.NotEmpty(tokens);
    }

    [Fact]
    public void ReportIntegration_IncludesEmbeddingScores()
    {
        var embedder = new SourceTokenEmbedder();
        var cohort = new CommandHandlerCohort();
        var allTypes = cohort.DiscoverTypes();
        var exemplarTypes = allTypes.Where(t => cohort.ExemplarTypeNames.Contains(t.Name)).ToList();

        var scores = EmbeddingSimilarityScorer.ScoreAll(allTypes, exemplarTypes, embedder);

        Assert.Equal(allTypes.Count, scores.Count);
        Assert.True(scores.All(s => s.CosineSimilarity >= -1.0 && s.CosineSimilarity <= 1.0));
        Assert.True(scores.Max(s => s.CosineSimilarity) > scores.Min(s => s.CosineSimilarity),
            "Embedding scores should show variation across handlers");
    }

    private class ExemplarAStub { }
    private class ExemplarBStub { }
    private class SimilarStub { }
    private class DissimilarStub { }

    private sealed class FixedEmbedder : ICodeEmbedder
    {
        private readonly Dictionary<string, double[]> _vectors;

        public FixedEmbedder(Dictionary<string, double[]> vectors)
        {
            _vectors = vectors;
            Dimensions = vectors.Values.First().Length;
        }

        public int Dimensions { get; }

        public double[] Embed(Type type)
        {
            var key = type.Name.Replace("Stub", "");
            return _vectors.TryGetValue(key, out var vector)
                ? vector
                : new double[Dimensions];
        }
    }
}
