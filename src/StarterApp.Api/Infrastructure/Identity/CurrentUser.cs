namespace StarterApp.Api.Infrastructure.Identity;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IReadOnlySet<string> _scopes;

    public CurrentUser(
        string subject,
        AuthenticatedPrincipalType principalType,
        string tenantId,
        IEnumerable<string> scopes,
        string correlationId,
        string? email,
        string? clientId,
        string? issuer)
    {
        Subject = subject;
        PrincipalType = principalType;
        TenantId = tenantId;
        _scopes = new HashSet<string>(scopes, StringComparer.Ordinal);
        CorrelationId = correlationId;
        Email = email;
        ClientId = clientId;
        Issuer = issuer;
    }

    public static CurrentUser Anonymous { get; } = new(
        string.Empty,
        AuthenticatedPrincipalType.User,
        string.Empty,
        Array.Empty<string>(),
        string.Empty,
        null,
        null,
        null);

    public bool IsAuthenticated => !string.IsNullOrEmpty(Subject);

    public string Subject { get; }

    public AuthenticatedPrincipalType PrincipalType { get; }

    public string TenantId { get; }

    public IReadOnlySet<string> Scopes => _scopes;

    public string CorrelationId { get; }

    public string? Email { get; }

    public string? ClientId { get; }

    public string? Issuer { get; }

    public bool HasScope(string scope)
    {
        return _scopes.Contains(scope);
    }
}
