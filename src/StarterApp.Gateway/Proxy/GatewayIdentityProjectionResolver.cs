using Microsoft.Extensions.Options;

namespace StarterApp.Gateway.Proxy;

// Resolves the identity the emulator projects, mirroring what a real APIM policy would derive from
// the caller's validated token: caller-stated X-Authenticated-* headers win per header (this is how
// tests and dev tools state identity), and the configured default dev identity fills the gaps.
internal sealed class GatewayIdentityProjectionResolver
{
    private readonly IOptions<GatewaySignerOptions> _options;

    public GatewayIdentityProjectionResolver(IOptions<GatewaySignerOptions> options)
    {
        _options = options;
    }

    public GatewayIdentityProjection Resolve(IHeaderDictionary incomingHeaders, string correlationId)
    {
        var options = _options.Value;

        var subject = ReadHeaderOrDefault(incomingHeaders, GatewayIdentityHeaderNames.Subject, options.DefaultSubject);
        var principalType = ReadHeaderOrDefault(incomingHeaders, GatewayIdentityHeaderNames.PrincipalType, options.DefaultPrincipalType);
        var tenantId = ReadHeaderOrDefault(incomingHeaders, GatewayIdentityHeaderNames.TenantId, options.DefaultTenantId);
        var scopes = ParseTokens(ReadHeaderOrDefault(incomingHeaders, GatewayIdentityHeaderNames.Scopes, options.DefaultScopes));
        var authenticationMethods = ParseTokens(ReadHeaderOrDefault(incomingHeaders, GatewayIdentityHeaderNames.AuthenticationMethods, options.DefaultAuthenticationMethods));

        return new GatewayIdentityProjection(
            subject,
            principalType,
            tenantId,
            scopes,
            authenticationMethods,
            correlationId);
    }

    private static string ReadHeaderOrDefault(IHeaderDictionary headers, string name, string defaultValue)
    {
        return ReadOptionalHeader(headers, name) ?? defaultValue;
    }

    private static string? ReadOptionalHeader(IHeaderDictionary headers, string name)
    {
        if (!headers.TryGetValue(name, out var values) || values.Count != 1)
            return null;

        var value = values[0];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // Same normalization the API's header parser applies, so the projected header and the signed
    // scp/amr claims agree on one ordered, de-duplicated form.
    private static string[] ParseTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();
    }
}
