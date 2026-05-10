namespace StarterApp.Api.Infrastructure.Identity;

public interface IOwnerOnlyPolicy
{
    OwnerScope GetRequiredScope();
    void Authorize(string ownerSubject, string tenantId);
}

public sealed record OwnerScope(string OwnerSubject, string TenantId);

public sealed class OwnerOnlyPolicy : IOwnerOnlyPolicy
{
    private readonly ICurrentUser _currentUser;

    public OwnerOnlyPolicy(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public OwnerScope GetRequiredScope()
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("A valid gateway identity is required.");

        return new OwnerScope(_currentUser.Subject, _currentUser.TenantId);
    }

    public void Authorize(string ownerSubject, string tenantId)
    {
        var scope = GetRequiredScope();
        if (string.Equals(ownerSubject, scope.OwnerSubject, StringComparison.Ordinal) &&
            string.Equals(tenantId, scope.TenantId, StringComparison.Ordinal))
        {
            return;
        }

        throw new ForbiddenAccessException("The current identity does not own this resource.");
    }
}
