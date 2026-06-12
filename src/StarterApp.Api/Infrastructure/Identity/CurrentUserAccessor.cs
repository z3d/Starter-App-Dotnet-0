namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class CurrentUserAccessor : ICurrentUser
{
    private CurrentUser _currentUser = CurrentUser.Anonymous;

    public bool IsAuthenticated => _currentUser.IsAuthenticated;

    public string Subject => _currentUser.Subject;

    public AuthenticatedPrincipalType PrincipalType => _currentUser.PrincipalType;

    public string TenantId => _currentUser.TenantId;

    public IReadOnlySet<string> Scopes => _currentUser.Scopes;

    public IReadOnlySet<string> AuthenticationMethods => _currentUser.AuthenticationMethods;

    public string CorrelationId => _currentUser.CorrelationId;

    public void Set(CurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public bool HasScope(string scope)
    {
        return _currentUser.HasScope(scope);
    }

    public bool HasAuthenticationMethod(string authenticationMethod)
    {
        return _currentUser.HasAuthenticationMethod(authenticationMethod);
    }
}
