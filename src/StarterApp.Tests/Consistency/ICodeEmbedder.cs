namespace StarterApp.Tests.Consistency;

/// <summary>
/// Generates a dense vector embedding for a code artifact identified by Type.
/// Implementations may use a local model, an API, or a deterministic stub.
///
/// The embedding captures semantic meaning — what the code does — as opposed to
/// structural shape (fingerprint) or control-flow skeleton (shingles).
/// Two handlers with identical structure but different domain purposes should
/// produce different embeddings.
/// </summary>
public interface ICodeEmbedder
{
    /// <summary>
    /// Returns a dense vector for the given type. Dimensionality is implementation-defined
    /// but must be consistent across calls within the same embedder instance.
    /// </summary>
    double[] Embed(Type type);

    /// <summary>
    /// The dimensionality of vectors produced by this embedder.
    /// </summary>
    int Dimensions { get; }
}
