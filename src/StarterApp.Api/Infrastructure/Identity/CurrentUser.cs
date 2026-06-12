namespace StarterApp.Api.Infrastructure.Identity;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IReadOnlySet<string> _scopes;
    private readonly IReadOnlySet<string> _authenticationMethods;

    public CurrentUser(
        string subject,
        AuthenticatedPrincipalType principalType,
        string tenantId,
        IEnumerable<string> scopes,
        string correlationId,
        IEnumerable<string>? authenticationMethods = null)
    {
        Subject = subject;
        PrincipalType = principalType;
        TenantId = tenantId;
        _scopes = new HashSet<string>(scopes, StringComparer.Ordinal);
        _authenticationMethods = new HashSet<string>(authenticationMethods ?? Array.Empty<string>(), StringComparer.Ordinal);
        CorrelationId = correlationId;
    }

    public static CurrentUser Anonymous { get; } = new(
        string.Empty,
        AuthenticatedPrincipalType.User,
        string.Empty,
        Array.Empty<string>(),
        string.Empty);

    public bool IsAuthenticated => !string.IsNullOrEmpty(Subject);

    public string Subject { get; }

    public AuthenticatedPrincipalType PrincipalType { get; }

    public string TenantId { get; }

    public IReadOnlySet<string> Scopes => _scopes;

    public IReadOnlySet<string> AuthenticationMethods => _authenticationMethods;

    public string CorrelationId { get; }

    public bool HasScope(string scope)
    {
        return _scopes.Contains(scope);
    }

    public bool HasAuthenticationMethod(string authenticationMethod)
    {
        return _authenticationMethods.Contains(authenticationMethod);
    }
}
