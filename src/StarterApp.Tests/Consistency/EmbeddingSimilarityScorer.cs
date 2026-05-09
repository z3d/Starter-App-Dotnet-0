namespace StarterApp.Tests.Consistency;

/// <summary>
/// Scores cohort members by cosine similarity of their token vectors to the exemplar centroid.
///
/// This is the third measurement layer from the consistency paper:
/// - Structural fingerprint (Mahalanobis): proportion and complexity outliers
/// - AST shingles (Jaccard): skeleton novelty
/// - Source-token similarity (cosine): vocabulary novelty — a member that refers to
///   different domain types, methods, strings, or metadata tokens than the exemplars
///
/// Low similarity = vocabulary novelty relative to the exemplar set. It is not a
/// judgement about business intent.
/// </summary>
public static class EmbeddingSimilarityScorer
{
    /// <summary>
    /// Computes the centroid of the exemplar token vectors.
    /// </summary>
    public static double[] ComputeCentroid(IReadOnlyList<double[]> exemplarEmbeddings)
    {
        if (exemplarEmbeddings.Count == 0)
            throw new ArgumentException("At least one exemplar embedding is required.", nameof(exemplarEmbeddings));

        var dim = exemplarEmbeddings[0].Length;
        var centroid = new double[dim];

        foreach (var embedding in exemplarEmbeddings)
            for (var i = 0; i < dim; i++)
                centroid[i] += embedding[i];

        for (var i = 0; i < dim; i++)
            centroid[i] /= exemplarEmbeddings.Count;

        return centroid;
    }

    /// <summary>
    /// Cosine similarity between two vectors. Returns a value in [-1, 1].
    /// 1.0 = identical direction, 0.0 = orthogonal, -1.0 = opposite.
    /// </summary>
    public static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimensionality.");

        var dot = 0.0;
        var normA = 0.0;
        var normB = 0.0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator < 1e-20 ? 0.0 : dot / denominator;
    }

    /// <summary>
    /// Scores all cohort members against the exemplar token-vector centroid.
    /// Returns results sorted by similarity ascending (most vocabulary-novel first).
    /// </summary>
    public static IReadOnlyList<EmbeddingScore> ScoreAll(
        IReadOnlyList<Type> allTypes,
        IReadOnlyList<Type> exemplarTypes,
        ICodeEmbedder embedder)
    {
        var exemplarEmbeddings = exemplarTypes.Select(t => embedder.Embed(t)).ToList();
        var centroid = ComputeCentroid(exemplarEmbeddings);

        return allTypes
            .Select(t =>
            {
                var embedding = embedder.Embed(t);
                var similarity = CosineSimilarity(embedding, centroid);
                return new EmbeddingScore(t.Name, similarity);
            })
            .OrderBy(s => s.CosineSimilarity)
            .ToList();
    }
}

public record EmbeddingScore(string TypeName, double CosineSimilarity);
