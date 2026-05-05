namespace StarterApp.Api.Infrastructure.Identity;

public static class GatewayIdentityEndpointExtensions
{
    public static RouteGroupBuilder RequireGatewayIdentity(this RouteGroupBuilder builder)
    {
        builder.WithMetadata(GatewayIdentityRequiredMetadata.Instance)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return builder;
    }
}
