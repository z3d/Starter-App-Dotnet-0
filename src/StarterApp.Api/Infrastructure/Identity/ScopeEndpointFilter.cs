namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class ScopeEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var requiredScopes = context.HttpContext.GetEndpoint()
            ?.Metadata
            .GetOrderedMetadata<ScopeRequiredMetadata>() ?? [];

        if (requiredScopes.Count == 0)
            return await next(context);

        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is not { IsAuthenticated: true })
            return BearerChallenges.Unauthenticated(context);

        var scopes = requiredScopes
            .Select(metadata => metadata.Scope)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var missingScope = scopes.FirstOrDefault(scope => !currentUser.HasScope(scope));

        // The challenge advertises the endpoint's full required scope set (RFC 6750 §3.1), so a
        // client can re-authorize once rather than discovering missing scopes one 403 at a time.
        return missingScope == null
            ? await next(context)
            : BearerChallenges.Forbidden(context, "Forbidden", $"Required scope '{missingScope}' is missing.",
                BearerChallenges.InsufficientScope(scopes));
    }
}
