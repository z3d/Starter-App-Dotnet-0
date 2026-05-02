namespace StarterApp.Tests.Consistency;

/// <summary>
/// Structural feature vector for a query handler.
/// </summary>
/// <remarks>
/// Features were chosen so that two handlers differing on any one of them would look
/// materially different to a reviewer. <see cref="IsCacheable"/> and
/// <see cref="ReturnsList"/> are kept as independent dimensions so the composite picks
/// up their crossing — the anti-pattern CLAUDE.md warns about is a list query that's
/// cacheable, and independent features let the distance score surface that combination
/// even when neither feature is unusual on its own.
/// </remarks>
public record QueryHandlerFingerprint : ICohortFingerprint
{
    public required string TypeName { get; init; }
    public required int IlByteSize { get; init; }
    public required int ConstructorDependencyCount { get; init; }
    public required bool HasPagination { get; init; }
    public required bool IsCacheable { get; init; }
    public required bool ReturnsList { get; init; }
    public required int JoinCount { get; init; }
    public required int SqlStatementCount { get; init; }

    public double[] ToVector() =>
    [
        IlByteSize,
        ConstructorDependencyCount,
        HasPagination ? 1.0 : 0.0,
        IsCacheable ? 1.0 : 0.0,
        ReturnsList ? 1.0 : 0.0,
        JoinCount,
        SqlStatementCount
    ];

    public string[] FeatureNames =>
    [
        "IlByteSize",
        "ConstructorDependencyCount",
        "HasPagination",
        "IsCacheable",
        "ReturnsList",
        "JoinCount",
        "SqlStatementCount"
    ];

    public FeatureKind[] FeatureKinds =>
    [
        FeatureKind.Numeric,   // IlByteSize
        FeatureKind.Numeric,   // ConstructorDependencyCount
        FeatureKind.Boolean,   // HasPagination
        FeatureKind.Boolean,   // IsCacheable
        FeatureKind.Boolean,   // ReturnsList
        FeatureKind.Numeric,   // JoinCount
        FeatureKind.Numeric    // SqlStatementCount
    ];
}
