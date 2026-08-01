namespace StarterApp.Api.Infrastructure.Identity;

public static class IdentityEndpointExtensions
{
    public static RouteHandlerBuilder RequireScope(this RouteHandlerBuilder builder, string scope)
    {
        builder.WithMetadata(new ScopeRequiredMetadata(scope))
            .AddEndpointFilter<ScopeEndpointFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return builder;
    }

    public static RouteHandlerBuilder SecuredBy2Fa(this RouteHandlerBuilder builder)
    {
        builder.WithMetadata(new TwoFactorRequiredMetadata(TwoFactorEndpointFilter.RequiredAuthenticationMethod))
            .AddEndpointFilter<TwoFactorEndpointFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return builder;
    }
}
