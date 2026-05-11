namespace StarterApp.Api.Infrastructure.Identity;

public static class GatewayIdentityEndpointExtensions
{
    public static RouteGroupBuilder RequireGatewayIdentity(this RouteGroupBuilder builder)
    {
        builder.WithMetadata(GatewayIdentityRequiredMetadata.Instance)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return builder;
    }

    public static RouteHandlerBuilder RequireScope(this RouteHandlerBuilder builder, string scope)
    {
        builder.WithMetadata(new GatewayScopeRequiredMetadata(scope))
            .AddEndpointFilter<GatewayScopeEndpointFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return builder;
    }

    public static RouteHandlerBuilder SecuredBy2Fa(this RouteHandlerBuilder builder)
    {
        builder.WithMetadata(new GatewayTwoFactorRequiredMetadata(GatewayTwoFactorEndpointFilter.RequiredAuthenticationMethod))
            .AddEndpointFilter<GatewayTwoFactorEndpointFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return builder;
    }
}
