namespace StarterApp.Api.Infrastructure.Identity;

// RFC 6750 §3 and RFC 9470 §3 challenge formats; inputs are constants and deployer config, so quoting suffices.
internal static class BearerChallenges
{
    // Unauthenticated here means the token validated but the identity mapping rejected it: invalid_token, not a missing header.
    public static IResult Unauthenticated(EndpointFilterInvocationContext context) =>
        Problem(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required.",
            InvalidToken("The token does not satisfy the identity claim contract."));

    public static IResult Forbidden(EndpointFilterInvocationContext context, string title, string detail, string challenge) =>
        Problem(context, StatusCodes.Status403Forbidden, title, detail, challenge);

    private static IResult Problem(EndpointFilterInvocationContext context, int statusCode, string title, string detail, string challenge)
    {
        context.HttpContext.Response.Headers.WWWAuthenticate = challenge;
        return Results.Problem(statusCode: statusCode, title: title, detail: detail);
    }

    public static string InvalidToken(string description) =>
        $"Bearer error=\"invalid_token\", error_description=\"{description}\"";

    public static string InsufficientScope(IEnumerable<string> requiredScopes) =>
        $"Bearer error=\"insufficient_scope\", scope=\"{string.Join(' ', requiredScopes)}\"";

    public static string InsufficientUserAuthentication(string? acrValues) =>
        string.IsNullOrWhiteSpace(acrValues)
            ? "Bearer error=\"insufficient_user_authentication\""
            : $"Bearer error=\"insufficient_user_authentication\", acr_values=\"{acrValues}\"";
}
