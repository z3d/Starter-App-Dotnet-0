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
    private readonly OwnerPolicyEvaluationTracker _tracker;

    public OwnerOnlyPolicy(ICurrentUser currentUser, OwnerPolicyEvaluationTracker tracker)
    {
        _currentUser = currentUser;
        _tracker = tracker;
    }

    public OwnerScope GetRequiredScope()
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("A valid gateway identity is required.");

        return new OwnerScope(_currentUser.Subject, _currentUser.TenantId);
    }

    public void Authorize(string ownerSubject, string tenantId)
    {
        // Marked before the comparison: a Forbidden outcome still counts as the
        // policy having been consulted (the request fails on its own).
        _tracker.MarkEvaluated();

        var scope = GetRequiredScope();
        if (string.Equals(ownerSubject, scope.OwnerSubject, StringComparison.Ordinal) &&
            string.Equals(tenantId, scope.TenantId, StringComparison.Ordinal))
        {
            return;
        }

        throw new ForbiddenAccessException("The current identity does not own this resource.");
    }
}
