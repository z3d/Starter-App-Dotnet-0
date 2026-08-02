namespace StarterApp.Api.Infrastructure.Identity;

// WWW-Authenticate challenge values for the identity endpoint filters, so a token shortfall is
// machine-actionable: a client reads the error code (and optional parameters), re-authorizes at
// the IdP, and retries — the API never proxies elevation. Formats per RFC 6750 §3
// (invalid_token, insufficient_scope) and RFC 9470 §3 (insufficient_user_authentication).
// Inputs are compile-time scope constants and deployer config, so quoting is sufficient.
internal static class BearerChallenges
{
    public static string InvalidToken(string description) =>
        $"Bearer error=\"invalid_token\", error_description=\"{description}\"";

    public static string InsufficientScope(IEnumerable<string> requiredScopes) =>
        $"Bearer error=\"insufficient_scope\", scope=\"{string.Join(' ', requiredScopes)}\"";

    public static string InsufficientUserAuthentication(string? acrValues) =>
        string.IsNullOrWhiteSpace(acrValues)
            ? "Bearer error=\"insufficient_user_authentication\""
            : $"Bearer error=\"insufficient_user_authentication\", acr_values=\"{acrValues}\"";
}
