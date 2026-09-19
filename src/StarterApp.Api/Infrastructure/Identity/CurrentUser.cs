namespace StarterApp.Api.Infrastructure.Identity;

public sealed class CurrentUser : ICurrentUser
{
    public CurrentUser(
        string subject,
        AuthenticatedPrincipalType principalType,
        string tenantId,
        IEnumerable<string> scopes,
        IEnumerable<string>? authenticationMethods = null)
    {
        Subject = subject;
        PrincipalType = principalType;
        TenantId = tenantId;
        Scopes = new HashSet<string>(scopes, StringComparer.Ordinal);
        AuthenticationMethods = new HashSet<string>(authenticationMethods ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    public static CurrentUser Anonymous { get; } = new(
        string.Empty,
        AuthenticatedPrincipalType.User,
        string.Empty,
        Array.Empty<string>());

    public bool IsAuthenticated => !string.IsNullOrEmpty(Subject);

    public string Subject { get; }

    public AuthenticatedPrincipalType PrincipalType { get; }

    public string TenantId { get; }

    public IReadOnlySet<string> Scopes { get; }

    public IReadOnlySet<string> AuthenticationMethods { get; }

    public bool HasScope(string scope)
    {
        return Scopes.Contains(scope);
    }

    public bool HasAuthenticationMethod(string authenticationMethod)
    {
        return AuthenticationMethods.Contains(authenticationMethod);
    }
}
