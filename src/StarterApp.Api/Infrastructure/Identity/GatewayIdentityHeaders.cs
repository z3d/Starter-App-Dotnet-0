using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.Identity;

public static class GatewayIdentityHeaders
{
    public const string Assertion = "X-Gateway-Assertion";
    public const string Subject = "X-Authenticated-Subject";
    public const string PrincipalType = "X-Authenticated-Principal-Type";
    public const string TenantId = "X-Authenticated-Tenant-Id";
    public const string Scopes = "X-Authenticated-Scopes";
    public const string AuthenticationMethods = "X-Authenticated-Amr";

    private const int MaxHeaderLength = 512;
    private const int MaxScopeLength = 100;
    private const int MaxScopes = 50;
    private const int MaxAuthenticationMethodLength = 64;
    private const int MaxAuthenticationMethods = 20;
    private const int MaxCorrelationIdLength = 128;

    private static readonly HashSet<string> AcceptedAuthenticatedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        Subject,
        PrincipalType,
        TenantId,
        Scopes,
        AuthenticationMethods
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
        var authenticationMethodsValue = ReadOptionalHeader(headers, AuthenticationMethods, errors);

        if (!Enum.TryParse<AuthenticatedPrincipalType>(principalTypeValue, ignoreCase: false, out var parsedPrincipalType))
            errors.Add($"{PrincipalType} must be either User or Service.");

        var scopes = ParseScopes(scopesValue, errors);
        var authenticationMethods = ParseAuthenticationMethods(authenticationMethodsValue, errors);
        ValidateCorrelationId(correlationId, errors);

        if (errors.Count > 0)
            return GatewayIdentityReadResult.Failure(errors);

        // Store the raw (already contract-validated) correlation id. The gateway signs the assertion
        // over this raw value, so the verifier must canonicalize identically — applying Sanitize() here
        // would compare against a rewritten value the external signer never saw and reject valid traffic.
        // Because ValidateCorrelationId enforces the same [A-Za-z0-9._-]{1,128} charset Sanitize keeps,
        // the stored value is already blob-path safe.
        var user = new CurrentUser(
            subject,
            parsedPrincipalType,
            tenantId,
            scopes,
            correlationId,
            authenticationMethods);

        return GatewayIdentityReadResult.Success(new GatewayIdentityEnvelope(user));
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
            if (scope.Length > MaxScopeLength || scope.Any(character => !IsIdentityTokenCharacter(character)))
                errors.Add($"{Scopes} contains invalid scope '{scope}'.");
        }

        return scopes;
    }

    private static string[] ParseAuthenticationMethods(string? authenticationMethodsValue, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(authenticationMethodsValue))
            return Array.Empty<string>();

        var authenticationMethods = authenticationMethodsValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(authenticationMethod => authenticationMethod, StringComparer.Ordinal)
            .ToArray();

        if (authenticationMethods.Length > MaxAuthenticationMethods)
            errors.Add($"{AuthenticationMethods} must contain no more than {MaxAuthenticationMethods} authentication methods.");

        foreach (var authenticationMethod in authenticationMethods)
        {
            if (authenticationMethod.Length > MaxAuthenticationMethodLength || authenticationMethod.Any(character => !IsIdentityTokenCharacter(character)))
                errors.Add($"{AuthenticationMethods} contains invalid authentication method '{authenticationMethod}'.");
        }

        return authenticationMethods;
    }

    private static void ValidateCorrelationId(string correlationId, ICollection<string> errors)
    {
        if (string.IsNullOrEmpty(correlationId))
            return;

        if (correlationId.Length > MaxCorrelationIdLength)
            errors.Add($"{CorrelationContext.HeaderName} exceeds {MaxCorrelationIdLength} characters.");

        if (correlationId.Any(character => !IsCorrelationIdCharacter(character)))
            errors.Add($"{CorrelationContext.HeaderName} must contain only letters, digits, '-', '_', or '.'.");
    }

    private static bool IsCorrelationIdCharacter(char character)
    {
        return character is >= 'A' and <= 'Z' ||
            character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character is '-' or '_' or '.';
    }

    private static bool IsSafeSingleHeaderValue(string value)
    {
        return !value.Contains('\r', StringComparison.Ordinal) &&
            !value.Contains('\n', StringComparison.Ordinal) &&
            !value.Contains(',', StringComparison.Ordinal);
    }

    private static bool IsIdentityTokenCharacter(char character)
    {
        return character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character is ':' or '.' or '_' or '-';
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
