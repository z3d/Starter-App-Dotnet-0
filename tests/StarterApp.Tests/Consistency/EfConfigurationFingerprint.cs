namespace StarterApp.Tests.Consistency;

/// <summary>
/// Structural feature vector for an EF Core entity configuration
/// (<c>IEntityTypeConfiguration&lt;T&gt;</c> implementation).
/// </summary>
/// <remarks>
/// Features are all counts rather than booleans because configs don't have natural
/// binary properties — every meaningful dimension is "how many of this DSL call does
/// it make." HasOne is deliberately omitted: an audit of the cohort showed zero
/// usages. Adding it would be a constant-zero feature that contributes nothing.
/// </remarks>
public record EfConfigurationFingerprint : ICohortFingerprint
{
    public required string TypeName { get; init; }
    public required int IlByteSize { get; init; }
    public required int OwnsOneCount { get; init; }
    public required int HasIndexCount { get; init; }
    public required int PropertyConfigCount { get; init; }
    public required int HasConversionCount { get; init; }
    public required int HasManyCount { get; init; }

    public double[] ToVector() =>
    [
        IlByteSize,
        OwnsOneCount,
        HasIndexCount,
        PropertyConfigCount,
        HasConversionCount,
        HasManyCount
    ];

    public string[] FeatureNames =>
    [
        "IlByteSize",
        "OwnsOneCount",
        "HasIndexCount",
        "PropertyConfigCount",
        "HasConversionCount",
        "HasManyCount"
    ];

    public FeatureKind[] FeatureKinds =>
    [
        FeatureKind.Numeric,
        FeatureKind.Numeric,
        FeatureKind.Numeric,
        FeatureKind.Numeric,
        FeatureKind.Numeric,
        FeatureKind.Numeric
    ];
}
