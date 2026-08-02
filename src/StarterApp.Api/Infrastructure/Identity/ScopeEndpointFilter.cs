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

        // Reaching this filter unauthenticated means the bearer handler accepted the token but
        // the identity contract mapping rejected it (e.g. missing sub/tid) — invalid_token, not
        // a missing Authorization header (that 401s at the authorization middleware).
        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is not { IsAuthenticated: true })
            return WriteProblem(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required.",
                BearerChallenges.InvalidToken("The token does not satisfy the identity claim contract."));

        var scopes = requiredScopes
            .Select(metadata => metadata.Scope)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var missingScope = scopes.FirstOrDefault(scope => !currentUser.HasScope(scope));

        // The challenge advertises the endpoint's full required scope set (RFC 6750 §3.1), so a
        // client can re-authorize once rather than discovering missing scopes one 403 at a time.
        return missingScope == null
            ? await next(context)
            : WriteProblem(context, StatusCodes.Status403Forbidden, "Forbidden", $"Required scope '{missingScope}' is missing.",
                BearerChallenges.InsufficientScope(scopes));
    }

    private static IResult WriteProblem(EndpointFilterInvocationContext context, int statusCode, string title, string detail, string challenge)
    {
        context.HttpContext.Response.Headers.WWWAuthenticate = challenge;
        return Results.Problem(statusCode: statusCode, title: title, detail: detail);
    }
}
