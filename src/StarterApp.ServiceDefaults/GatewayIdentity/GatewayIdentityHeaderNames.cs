namespace StarterApp.ServiceDefaults.GatewayIdentity;

// The single source of truth for the gateway identity header contract: the names a trusted gateway
// projects and the API verifies. Shared in ServiceDefaults so the API's verifier and the dev gateway
// emulator (StarterApp.Gateway) reference the same literals and can never drift apart — a real APIM
// policy must project these same names.
public static class GatewayIdentityHeaderNames
{
    public const string Assertion = "X-Gateway-Assertion";
    public const string Subject = "X-Authenticated-Subject";
    public const string PrincipalType = "X-Authenticated-Principal-Type";
    public const string TenantId = "X-Authenticated-Tenant-Id";
    public const string Scopes = "X-Authenticated-Scopes";
    public const string AuthenticationMethods = "X-Authenticated-Amr";
}
