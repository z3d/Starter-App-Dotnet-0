namespace StarterApp.Tests.Consistency;

/// <summary>
/// Structural feature vector for a command handler.
/// Each dimension captures one aspect of handler "shape".
/// </summary>
/// <remarks>
/// <see cref="IlByteSize"/> is a complexity proxy, not a source line count. It's the summed
/// IL body size across the handler's methods (including async state machines) — empirically
/// 2-4x the source line count with a per-handler ratio that varies, so the value is only
/// meaningful relative to other handlers in the same cohort.
/// </remarks>
public record HandlerFingerprint : ICohortFingerprint
{
    public required string TypeName { get; init; }
    public required int IlByteSize { get; init; }
    public required int ConstructorDependencyCount { get; init; }
    public required bool HasLogger { get; init; }
    public required bool HasCacheInvalidator { get; init; }
    public required bool HasTryCatch { get; init; }
    public required int PrivateMethodCount { get; init; }
    public required int EntityLoadCount { get; init; }

    public double[] ToVector() =>
    [
        IlByteSize,
        ConstructorDependencyCount,
        HasLogger ? 1.0 : 0.0,
        HasCacheInvalidator ? 1.0 : 0.0,
        HasTryCatch ? 1.0 : 0.0,
        PrivateMethodCount,
        EntityLoadCount
    ];

    public string[] FeatureNames =>
    [
        "IlByteSize",
        "ConstructorDependencyCount",
        "HasLogger",
        "HasCacheInvalidator",
        "HasTryCatch",
        "PrivateMethodCount",
        "EntityLoadCount"
    ];

    public FeatureKind[] FeatureKinds =>
    [
        FeatureKind.Numeric,   // IlByteSize
        FeatureKind.Numeric,   // ConstructorDependencyCount
        FeatureKind.Boolean,   // HasLogger
        FeatureKind.Boolean,   // HasCacheInvalidator
        FeatureKind.Boolean,   // HasTryCatch
        FeatureKind.Numeric,   // PrivateMethodCount
        FeatureKind.Numeric    // EntityLoadCount
    ];
}
