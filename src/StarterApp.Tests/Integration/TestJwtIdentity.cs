using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StarterApp.Tests.Integration;

// Self-issued RSA test JWTs: the suite validates tokens through the real JwtBearer handler with
// the same asymmetric algorithm as production (RS256), but against an in-memory signing key
// instead of a live IdP — hermetic and offline. ConfigureTestJwtValidation pins the handler's
// TokenValidationParameters to the test issuer/key; CreateToken mints tokens with overridable
// claims for negative-path tests (wrong issuer/audience/key, expiry, missing scopes or amr).
internal static class TestJwtIdentity
{
    public const string Issuer = "https://test-idp.example.test/realms/starterapp-tests";
    public const string Audience = "starterapp-api-tests";

    public const string DefaultSubject = "test-user-01";
    public const string DefaultTenantId = "test-tenant-01";
    public const string DefaultScopes = "customers:read customers:write orders:read orders:write products:read products:write";
    public const string DefaultAuthenticationMethods = "mfa pwd";

    public static RsaSecurityKey SigningKey { get; } = CreateKey("test-key-1");

    // A second key the handler does not trust — for bad-signature tests.
    public static RsaSecurityKey UntrustedKey { get; } = CreateKey("untrusted-key-1");

    public static IReadOnlyDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>
    {
        // No Authority: ConfigureTestJwtValidation injects the issuer and signing key directly,
        // so no discovery/JWKS fetch happens in tests.
        ["Identity:Audience"] = Audience,
        ["Identity:ClockSkewSeconds"] = "0"
    };

    public static void ConfigureTestJwtValidation(IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.ConfigurationManager = null;
            options.TokenValidationParameters.ValidIssuer = Issuer;
            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
        });
    }

    public static string CreateToken(
        string subject = DefaultSubject,
        string? tenantId = DefaultTenantId,
        string? scopes = DefaultScopes,
        string? authenticationMethods = DefaultAuthenticationMethods,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? notBefore = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject
        };

        if (tenantId != null)
            claims["tid"] = tenantId;

        if (scopes != null)
            claims["scope"] = scopes;

        if (authenticationMethods != null)
            claims["amr"] = authenticationMethods.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore ?? now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(5),
            IssuedAt = notBefore ?? now.AddMinutes(-1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static void SetBearer(
        HttpRequestMessage request,
        string subject = DefaultSubject,
        string tenantId = DefaultTenantId,
        string? scopes = DefaultScopes,
        string? authenticationMethods = DefaultAuthenticationMethods)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(subject, tenantId, scopes, authenticationMethods));
    }

    private static RsaSecurityKey CreateKey(string keyId)
    {
        return new RsaSecurityKey(RSA.Create(2048)) { KeyId = keyId };
    }
}

// Attaches a default-identity bearer token to every outbound request; individual tests override
// by setting request.Headers.Authorization themselves (SetBearer or a hand-built token).
internal sealed class JwtSigningHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization ??= new AuthenticationHeaderValue("Bearer", TestJwtIdentity.CreateToken());
        return base.SendAsync(request, cancellationToken);
    }
}
