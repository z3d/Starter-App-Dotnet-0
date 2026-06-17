namespace StarterApp.Gateway.Proxy;

public sealed class GatewaySignerOptions
{
    public const string SectionName = "GatewaySigner";

    public string Issuer { get; set; } = "apim";

    public string Audience { get; set; } = "starterapp-api";

    public string SigningKey { get; set; } = string.Empty;

    public string? KeyId { get; set; }

    // Must stay at or below the API's GatewayIdentity:MaxTokenLifetimeSeconds (120).
    public int TokenLifetimeSeconds { get; set; } = 60;

    public string DefaultSubject { get; set; } = "local-dev-user";

    public string DefaultPrincipalType { get; set; } = "User";

    public string DefaultTenantId { get; set; } = "local-dev-tenant";

    // The default dev identity must cover every endpoint scope so a zero-setup caller can reach the
    // whole API surface; keep in sync with the RequireScope declarations on the endpoints.
    public string DefaultScopes { get; set; } = "customers:read customers:write orders:read orders:write products:read products:write";

    public string DefaultAuthenticationMethods { get; set; } = "mfa pwd";
}
