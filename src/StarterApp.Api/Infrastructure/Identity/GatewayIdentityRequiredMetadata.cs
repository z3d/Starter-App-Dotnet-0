namespace StarterApp.Api.Infrastructure.Identity;

public sealed class GatewayIdentityRequiredMetadata
{
    public static GatewayIdentityRequiredMetadata Instance { get; } = new();

    private GatewayIdentityRequiredMetadata()
    {
    }
}
