namespace StarterApp.Api.Infrastructure.Identity;

public sealed class ScopeRequiredMetadata
{
    public ScopeRequiredMetadata(string scope)
    {
        Scope = scope;
    }

    public string Scope { get; }
}
