using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Integration;

internal static class TestGatewayIdentity
{
    public const string Issuer = "test-apim";
    public const string Audience = "starterapp-api-tests";
    public const string KeyId = "test-key-1";
    public const string SigningKey = "starter-app-test-gateway-signing-key-32-bytes-minimum";

    private const string DefaultSubject = "test-user-01";
    private const string DefaultTenantId = "test-tenant-01";
    private const string DefaultScopes = "customers:read customers:write orders:read orders:write products:read products:write";

    public static IReadOnlyDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>
    {
        ["GatewayIdentity:Mode"] = "Required",
        ["GatewayIdentity:Issuer"] = Issuer,
        ["GatewayIdentity:Audience"] = Audience,
        ["GatewayIdentity:KeyId"] = KeyId,
        ["GatewayIdentity:SigningKey"] = SigningKey,
        ["GatewayIdentity:ClockSkewSeconds"] = "0",
        ["GatewayIdentity:MaxTokenLifetimeSeconds"] = "120"
    };

    public static void AddSignedHeaders(
        HttpRequestMessage request,
        string subject = DefaultSubject,
        string tenantId = DefaultTenantId,
        string scopes = DefaultScopes,
        string? path = null,
        string? method = null,
        string issuer = Issuer,
        string audience = Audience,
        string? keyId = KeyId,
        string? signingKey = null,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        SetHeader(request, GatewayIdentityHeaders.Subject, subject);
        SetHeader(request, GatewayIdentityHeaders.PrincipalType, AuthenticatedPrincipalType.User.ToString());
        SetHeader(request, GatewayIdentityHeaders.TenantId, tenantId);
        SetHeader(request, GatewayIdentityHeaders.Scopes, scopes);

        var correlationId = GetOrSetHeader(request, CorrelationContext.HeaderName, $"test-{Guid.NewGuid():N}");
        var currentUser = new CurrentUser(
            subject,
            AuthenticatedPrincipalType.User,
            tenantId,
            scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            correlationId,
            null,
            null,
            null);

        var now = issuedAt ?? DateTimeOffset.UtcNow;
        var payload = new GatewayAssertionPayload
        {
            Issuer = issuer,
            Audience = audience,
            Subject = currentUser.Subject,
            PrincipalType = currentUser.PrincipalType.ToString(),
            TenantId = currentUser.TenantId,
            Scopes = currentUser.Scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray(),
            CorrelationId = currentUser.CorrelationId,
            Method = method ?? request.Method.Method,
            Path = path ?? ResolvePath(request.RequestUri),
            HeaderHash = GatewayIdentityHeaders.ComputeHeaderHash(currentUser),
            IssuedAt = now.ToUnixTimeSeconds(),
            ExpiresAt = (expiresAt ?? now.AddSeconds(60)).ToUnixTimeSeconds(),
            KeyId = keyId
        };

        SetHeader(request, GatewayIdentityHeaders.Assertion, GatewayAssertionToken.Create(payload, signingKey ?? SigningKey));
    }

    public static void AddUnsignedHeaders(HttpRequestMessage request)
    {
        SetHeader(request, GatewayIdentityHeaders.Subject, DefaultSubject);
        SetHeader(request, GatewayIdentityHeaders.PrincipalType, AuthenticatedPrincipalType.User.ToString());
        SetHeader(request, GatewayIdentityHeaders.TenantId, DefaultTenantId);
        SetHeader(request, GatewayIdentityHeaders.Scopes, DefaultScopes);
        GetOrSetHeader(request, CorrelationContext.HeaderName, $"test-{Guid.NewGuid():N}");
    }

    private static string GetOrSetHeader(HttpRequestMessage request, string name, string value)
    {
        if (request.Headers.TryGetValues(name, out var values))
            return values.Single();

        request.Headers.Add(name, value);
        return value;
    }

    private static void SetHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.Add(name, value);
    }

    private static string ResolvePath(Uri? uri)
    {
        if (uri == null)
            return string.Empty;

        return uri.IsAbsoluteUri
            ? uri.AbsolutePath
            : uri.OriginalString.Split('?', 2)[0];
    }
}

internal sealed class GatewayIdentitySigningHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TestGatewayIdentity.AddSignedHeaders(request);
        return base.SendAsync(request, cancellationToken);
    }
}
