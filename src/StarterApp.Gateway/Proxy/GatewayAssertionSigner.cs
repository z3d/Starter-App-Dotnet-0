using Microsoft.Extensions.Options;

namespace StarterApp.Gateway.Proxy;

internal sealed class GatewayAssertionSigner
{
    private readonly IOptions<GatewaySignerOptions> _options;
    private readonly TimeProvider _timeProvider;

    public GatewayAssertionSigner(IOptions<GatewaySignerOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public string CreateAssertion(GatewayIdentityProjection projection, string method, string path)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();

        // Authentication methods (amr) are a first-class signed claim — SecuredBy2Fa trusts them —
        // so they ride the payload directly. There is no projected-header hash: every projected
        // field is signed individually, which the API's validator re-checks against the headers.
        var payload = new GatewayAssertionPayload
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = projection.Subject,
            PrincipalType = projection.PrincipalType,
            TenantId = projection.TenantId,
            Scopes = projection.Scopes.ToArray(),
            CorrelationId = projection.CorrelationId,
            Method = method,
            Path = path,
            AuthenticationMethods = projection.AuthenticationMethods.ToArray(),
            IssuedAt = now.ToUnixTimeSeconds(),
            ExpiresAt = now.AddSeconds(options.TokenLifetimeSeconds).ToUnixTimeSeconds(),
            KeyId = options.KeyId,
        };

        return GatewayAssertionToken.Create(payload, options.SigningKey);
    }
}
