namespace StarterApp.Gateway.Proxy;

// The normalized identity the emulator projects and signs. Mirrors exactly the contract the API's
// GatewayIdentityHeaders parser accepts — Subject, PrincipalType, TenantId, Scopes, authentication
// methods (amr) and the correlation id. The API rejects any other X-Authenticated-* header, so the
// projection deliberately carries nothing more.
internal sealed record GatewayIdentityProjection(
    string Subject,
    string PrincipalType,
    string TenantId,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> AuthenticationMethods,
    string CorrelationId);
