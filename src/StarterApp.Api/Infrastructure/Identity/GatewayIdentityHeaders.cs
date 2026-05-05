using StarterApp.ServiceDefaults.Payloads;
using System.Security.Cryptography;
using System.Text;

namespace StarterApp.Api.Infrastructure.Identity;

public static class GatewayIdentityHeaders
{
    public const string Assertion = "X-Gateway-Assertion";
    public const string Subject = "X-Authenticated-Subject";
    public const string PrincipalType = "X-Authenticated-Principal-Type";
    public const string TenantId = "X-Authenticated-Tenant-Id";
    public const string Scopes = "X-Authenticated-Scopes";
    public const string Email = "X-Authenticated-Email";
    public const string ClientId = "X-Authenticated-Client-Id";
    public const string Issuer = "X-Authenticated-Issuer";

    private const int MaxHeaderLength = 512;
    private const int MaxScopeLength = 100;
    private const int MaxScopes = 50;

    private static readonly HashSet<string> AcceptedAuthenticatedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        Subject,
        PrincipalType,
        TenantId,
        Scopes,
        Email,
        ClientId,
        Issuer
    };

    internal static GatewayIdentityReadResult Read(IHeaderDictionary headers)
    {
        var errors = new List<string>();
        RejectUnsupportedAuthenticatedHeaders(headers, errors);

        var subject = ReadRequiredHeader(headers, Subject, errors);
        var principalTypeValue = ReadRequiredHeader(headers, PrincipalType, errors);
        var tenantId = ReadRequiredHeader(headers, TenantId, errors);
        var scopesValue = ReadRequiredHeader(headers, Scopes, errors);
        var correlationId = ReadRequiredHeader(headers, CorrelationContext.HeaderName, errors);
        var email = ReadOptionalHeader(headers, Email, errors);
        var clientId = ReadOptionalHeader(headers, ClientId, errors);
        var issuer = ReadOptionalHeader(headers, Issuer, errors);

        if (!Enum.TryParse<AuthenticatedPrincipalType>(principalTypeValue, ignoreCase: false, out var parsedPrincipalType))
            errors.Add($"{PrincipalType} must be either User or Service.");

        var scopes = ParseScopes(scopesValue, errors);

        if (errors.Count > 0)
            return GatewayIdentityReadResult.Failure(errors);

        var user = new CurrentUser(
            subject,
            parsedPrincipalType,
            tenantId,
            scopes,
            CorrelationContext.Sanitize(correlationId),
            email,
            clientId,
            issuer);

        return GatewayIdentityReadResult.Success(new GatewayIdentityEnvelope(user, ComputeHeaderHash(user)));
    }

    internal static string ComputeHeaderHash(ICurrentUser user)
    {
        var builder = new StringBuilder();
        AppendCanonicalHeader(builder, Subject, user.Subject);
        AppendCanonicalHeader(builder, PrincipalType, user.PrincipalType.ToString());
        AppendCanonicalHeader(builder, TenantId, user.TenantId);
        AppendCanonicalHeader(builder, Scopes, string.Join(' ', user.Scopes.OrderBy(scope => scope, StringComparer.Ordinal)));
        AppendCanonicalHeader(builder, CorrelationContext.HeaderName, user.CorrelationId);
        AppendCanonicalHeader(builder, Email, user.Email);
        AppendCanonicalHeader(builder, ClientId, user.ClientId);
        AppendCanonicalHeader(builder, Issuer, user.Issuer);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return GatewayAssertionToken.Base64UrlEncode(hash);
    }

    private static void RejectUnsupportedAuthenticatedHeaders(IHeaderDictionary headers, ICollection<string> errors)
    {
        foreach (var header in headers.Keys)
        {
            if (header.StartsWith("X-Authenticated-", StringComparison.OrdinalIgnoreCase) && !AcceptedAuthenticatedHeaders.Contains(header))
                errors.Add($"Unsupported identity header '{header}' is not part of the gateway identity contract.");
        }
    }

    private static string ReadRequiredHeader(IHeaderDictionary headers, string name, ICollection<string> errors)
    {
        var value = ReadSingleHeader(headers, name, errors);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        errors.Add($"{name} is required.");
        return string.Empty;
    }

    private static string? ReadOptionalHeader(IHeaderDictionary headers, string name, ICollection<string> errors)
    {
        var value = ReadSingleHeader(headers, name, errors);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ReadSingleHeader(IHeaderDictionary headers, string name, ICollection<string> errors)
    {
        if (!headers.TryGetValue(name, out var values))
            return string.Empty;

        if (values.Count != 1)
        {
            errors.Add($"{name} must have exactly one value.");
            return string.Empty;
        }

        var value = values[0] ?? string.Empty;
        if (!IsSafeSingleHeaderValue(value))
        {
            errors.Add($"{name} contains an invalid header value.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxHeaderLength)
        {
            errors.Add($"{name} exceeds {MaxHeaderLength} characters.");
            return string.Empty;
        }

        return trimmed;
    }

    private static string[] ParseScopes(string scopesValue, ICollection<string> errors)
    {
        var scopes = scopesValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();

        if (scopes.Length == 0)
            errors.Add($"{Scopes} must contain at least one scope.");

        if (scopes.Length > MaxScopes)
            errors.Add($"{Scopes} must contain no more than {MaxScopes} scopes.");

        foreach (var scope in scopes)
        {
            if (scope.Length > MaxScopeLength || scope.Any(character => !IsScopeCharacter(character)))
                errors.Add($"{Scopes} contains invalid scope '{scope}'.");
        }

        return scopes;
    }

    private static bool IsSafeSingleHeaderValue(string value)
    {
        return !value.Contains('\r', StringComparison.Ordinal) &&
            !value.Contains('\n', StringComparison.Ordinal) &&
            !value.Contains(',', StringComparison.Ordinal);
    }

    private static bool IsScopeCharacter(char character)
    {
        return character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character is ':' or '.' or '_' or '-';
    }

    private static void AppendCanonicalHeader(StringBuilder builder, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        builder.Append(name.ToLowerInvariant())
            .Append('=')
            .Append(value.Trim())
            .Append('\n');
    }
}

internal sealed record GatewayIdentityReadResult(
    bool Succeeded,
    GatewayIdentityEnvelope? Envelope,
    IReadOnlyList<string> Errors)
{
    public static GatewayIdentityReadResult Success(GatewayIdentityEnvelope envelope)
    {
        return new GatewayIdentityReadResult(true, envelope, Array.Empty<string>());
    }

    public static GatewayIdentityReadResult Failure(IReadOnlyList<string> errors)
    {
        return new GatewayIdentityReadResult(false, null, errors);
    }
}
