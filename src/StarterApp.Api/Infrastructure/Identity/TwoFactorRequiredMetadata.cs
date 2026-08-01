namespace StarterApp.Api.Infrastructure.Identity;

public sealed class TwoFactorRequiredMetadata
{
    public TwoFactorRequiredMetadata(string authenticationMethod)
    {
        AuthenticationMethod = authenticationMethod;
    }

    public string AuthenticationMethod { get; }
}
