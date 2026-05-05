namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class CurrentUserAccessor : ICurrentUser
{
    private CurrentUser _currentUser = CurrentUser.Anonymous;

    public bool IsAuthenticated => _currentUser.IsAuthenticated;

    public string Subject => _currentUser.Subject;

    public AuthenticatedPrincipalType PrincipalType => _currentUser.PrincipalType;

    public string TenantId => _currentUser.TenantId;

    public IReadOnlySet<string> Scopes => _currentUser.Scopes;

    public string CorrelationId => _currentUser.CorrelationId;

    public string? Email => _currentUser.Email;

    public string? ClientId => _currentUser.ClientId;

    public string? Issuer => _currentUser.Issuer;

    public void Set(CurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public bool HasScope(string scope)
    {
        return _currentUser.HasScope(scope);
    }
}
