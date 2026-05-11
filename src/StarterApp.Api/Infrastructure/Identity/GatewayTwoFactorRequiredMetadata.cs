namespace StarterApp.Api.Infrastructure.Identity;

public sealed class GatewayTwoFactorRequiredMetadata
{
    public GatewayTwoFactorRequiredMetadata(string authenticationMethod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationMethod);
        AuthenticationMethod = authenticationMethod;
    }

    public string AuthenticationMethod { get; }
}
