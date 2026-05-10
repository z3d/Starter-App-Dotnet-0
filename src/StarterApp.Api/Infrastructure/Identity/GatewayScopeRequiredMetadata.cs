namespace StarterApp.Api.Infrastructure.Identity;

public sealed class GatewayScopeRequiredMetadata
{
    public GatewayScopeRequiredMetadata(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        Scope = scope;
    }

    public string Scope { get; }
}
