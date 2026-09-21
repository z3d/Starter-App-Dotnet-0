namespace StarterApp.Tests.Consistency;

/// <summary>
/// Fluent builder for <see cref="HandlerFingerprint"/> in tests. Replaces per-file
/// <c>MakeFingerprint</c> helpers with a single shared construction surface so new
/// features don't require editing every test file.
///
/// Defaults match the "typical exemplar" shape: has logger + cache invalidator, one
/// entity load, no try/catch, ~85 lines, 3 ctor dependencies. Override only what the
/// test exercises.
/// </summary>
public sealed class HandlerFingerprintBuilder
{
    private string _name = "Handler";
    private int _ilByteSize = 750;
    private int _deps = 3;
    private bool _hasLogger = true;
    private bool _hasCacheInvalidator = true;
    private bool _hasTryCatch;
    private int _privateMethods;
    private int _entityLoads = 1;

    public static HandlerFingerprintBuilder A() => new();

    public HandlerFingerprintBuilder Named(string name) { _name = name; return this; }
    public HandlerFingerprintBuilder WithIlByteSize(int ilByteSize) { _ilByteSize = ilByteSize; return this; }
    public HandlerFingerprintBuilder WithDeps(int deps) { _deps = deps; return this; }
    public HandlerFingerprintBuilder WithLogger(bool has = true) { _hasLogger = has; return this; }
    public HandlerFingerprintBuilder WithCacheInvalidator(bool has = true) { _hasCacheInvalidator = has; return this; }
    public HandlerFingerprintBuilder WithTryCatch(bool has = true) { _hasTryCatch = has; return this; }
    public HandlerFingerprintBuilder WithPrivateMethods(int count) { _privateMethods = count; return this; }
    public HandlerFingerprintBuilder WithEntityLoads(int count) { _entityLoads = count; return this; }

    public HandlerFingerprint Build() =>
        new()
        {
            TypeName = _name,
            IlByteSize = _ilByteSize,
            ConstructorDependencyCount = _deps,
            HasLogger = _hasLogger,
            HasCacheInvalidator = _hasCacheInvalidator,
            HasTryCatch = _hasTryCatch,
            PrivateMethodCount = _privateMethods,
            EntityLoadCount = _entityLoads
        };
}
