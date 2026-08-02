using Microsoft.Extensions.Options;

namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class TwoFactorEndpointFilter : IEndpointFilter
{
    public const string RequiredAuthenticationMethod = "mfa";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var requiredMethods = context.HttpContext.GetEndpoint()
            ?.Metadata
            .GetOrderedMetadata<TwoFactorRequiredMetadata>() ?? [];

        if (requiredMethods.Count == 0)
            return await next(context);

        // Reaching this filter unauthenticated means the bearer handler accepted the token but
        // the identity contract mapping rejected it (e.g. missing sub/tid) — invalid_token, not
        // a missing Authorization header (that 401s at the authorization middleware).
        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is not { IsAuthenticated: true })
            return WriteProblem(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required.",
                BearerChallenges.InvalidToken("The token does not satisfy the identity claim contract."));

        var missingAuthenticationMethod = requiredMethods
            .Select(metadata => metadata.AuthenticationMethod)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(authenticationMethod => !currentUser.HasAuthenticationMethod(authenticationMethod));

        if (missingAuthenticationMethod == null)
            return await next(context);

        // RFC 9470 step-up challenge: the client re-authorizes at the IdP (passing the advertised
        // acr_values when a deployer configured one) and retries with the stepped-up token. The
        // access check itself stays amr-based — acr_values here is advisory routing only.
        var acrValues = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<JwtIdentityOptions>>().Value.StepUpAcrValues;
        return WriteProblem(context, StatusCodes.Status403Forbidden, "Forbidden", "Two-factor authentication is required for this operation.",
            BearerChallenges.InsufficientUserAuthentication(acrValues));
    }

    private static IResult WriteProblem(EndpointFilterInvocationContext context, int statusCode, string title, string detail, string challenge)
    {
        context.HttpContext.Response.Headers.WWWAuthenticate = challenge;
        return Results.Problem(statusCode: statusCode, title: title, detail: detail);
    }
}
