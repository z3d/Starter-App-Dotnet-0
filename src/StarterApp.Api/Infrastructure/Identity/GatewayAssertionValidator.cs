using Microsoft.Extensions.Options;

namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class GatewayAssertionValidator : IGatewayAssertionValidator
{
    private readonly GatewayIdentityOptions _options;
    private readonly TimeProvider _timeProvider;

    public GatewayAssertionValidator(IOptions<GatewayIdentityOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public GatewayAssertionValidationResult Validate(HttpContext context, GatewayIdentityEnvelope envelope)
    {
        var assertion = ReadAssertionHeader(context.Request.Headers);
        if (assertion == null)
            return GatewayAssertionValidationResult.Failure("Gateway assertion is required.");

        if (!GatewayAssertionToken.TryRead(assertion, out var signingInput, out var payloadSegment, out var signatureSegment))
            return GatewayAssertionValidationResult.Failure("Gateway assertion format is invalid.");

        var payload = GatewayAssertionToken.ReadPayload(payloadSegment);
        if (payload == null)
            return GatewayAssertionValidationResult.Failure("Gateway assertion payload is invalid.");

        if (!string.IsNullOrWhiteSpace(_options.KeyId) && !string.Equals(payload.KeyId, _options.KeyId, StringComparison.Ordinal))
            return GatewayAssertionValidationResult.Failure("Gateway assertion key id is unknown.");

        if (string.IsNullOrWhiteSpace(_options.KeyId) && !string.IsNullOrWhiteSpace(payload.KeyId))
            return GatewayAssertionValidationResult.Failure("Gateway assertion key id is unknown.");

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || !GatewayAssertionToken.VerifySignature(signingInput, signatureSegment, _options.SigningKey))
            return GatewayAssertionValidationResult.Failure("Gateway assertion signature is invalid.");

        return ValidatePayload(context, envelope, payload);
    }

    private GatewayAssertionValidationResult ValidatePayload(HttpContext context, GatewayIdentityEnvelope envelope, GatewayAssertionPayload payload)
    {
        if (!string.Equals(payload.Issuer, _options.Issuer, StringComparison.Ordinal))
            return GatewayAssertionValidationResult.Failure("Gateway assertion issuer is invalid.");

        if (!string.Equals(payload.Audience, _options.Audience, StringComparison.Ordinal))
            return GatewayAssertionValidationResult.Failure("Gateway assertion audience is invalid.");

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var clockSkew = _options.ClockSkewSeconds;
        if (payload.IssuedAt > now + clockSkew)
            return GatewayAssertionValidationResult.Failure("Gateway assertion is not valid yet.");

        if (payload.ExpiresAt < now - clockSkew)
            return GatewayAssertionValidationResult.Failure("Gateway assertion has expired.");

        if (payload.ExpiresAt <= payload.IssuedAt || payload.ExpiresAt - payload.IssuedAt > _options.MaxTokenLifetimeSeconds)
            return GatewayAssertionValidationResult.Failure("Gateway assertion lifetime is invalid.");

        if (!string.Equals(payload.Method, context.Request.Method, StringComparison.OrdinalIgnoreCase))
            return GatewayAssertionValidationResult.Failure("Gateway assertion method does not match the request.");

        if (!string.Equals(payload.Path, context.Request.Path.Value ?? string.Empty, StringComparison.Ordinal))
            return GatewayAssertionValidationResult.Failure("Gateway assertion path does not match the request.");

        if (!string.Equals(payload.Subject, envelope.User.Subject, StringComparison.Ordinal) ||
            !string.Equals(payload.PrincipalType, envelope.User.PrincipalType.ToString(), StringComparison.Ordinal) ||
            !string.Equals(payload.TenantId, envelope.User.TenantId, StringComparison.Ordinal) ||
            !string.Equals(payload.CorrelationId, envelope.User.CorrelationId, StringComparison.Ordinal) ||
            !string.Equals(payload.HeaderHash, envelope.HeaderHash, StringComparison.Ordinal))
        {
            return GatewayAssertionValidationResult.Failure("Gateway assertion identity does not match the projected headers.");
        }

        var payloadScopes = payload.Scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray();
        var headerScopes = envelope.User.Scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray();
        if (!payloadScopes.SequenceEqual(headerScopes, StringComparer.Ordinal))
            return GatewayAssertionValidationResult.Failure("Gateway assertion scopes do not match the projected headers.");

        return GatewayAssertionValidationResult.Success();
    }

    private static string? ReadAssertionHeader(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(GatewayIdentityHeaders.Assertion, out var values) || values.Count != 1)
            return null;

        var value = values[0];
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.Contains(',', StringComparison.Ordinal))
            return null;

        return value.Trim();
    }
}

internal sealed record GatewayAssertionValidationResult(
    bool Succeeded,
    string? Error)
{
    public static GatewayAssertionValidationResult Success()
    {
        return new GatewayAssertionValidationResult(true, null);
    }

    public static GatewayAssertionValidationResult Failure(string error)
    {
        return new GatewayAssertionValidationResult(false, error);
    }
}
