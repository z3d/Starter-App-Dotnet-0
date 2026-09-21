namespace StarterApp.Tests.Consistency;

/// <summary>
/// A structural feature vector for a member of a cohort.
/// Implementations define the features relevant to their cohort.
/// </summary>
public interface ICohortFingerprint
{
    string TypeName { get; }
    double[] ToVector();
    string[] FeatureNames { get; }

    /// <summary>
    /// Declares whether each feature is boolean or numeric. This drives divergence
    /// analysis — boolean features use mode comparison, numeric features use sigma
    /// thresholds. Must be the same length as ToVector() and FeatureNames.
    /// </summary>
    FeatureKind[] FeatureKinds { get; }
}

public enum FeatureKind
{
    Numeric,
    Boolean
}
