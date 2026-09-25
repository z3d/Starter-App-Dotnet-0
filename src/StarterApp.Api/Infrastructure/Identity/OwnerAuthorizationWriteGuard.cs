using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StarterApp.Api.Infrastructure.Identity;

// Runs before SaveChanges and before ExecuteUpdate/ExecuteDelete, which the behaviour alone cannot see; reads are never blocked.
internal sealed class OwnerAuthorizationWriteGuard : DbCommandInterceptor, ISaveChangesInterceptor
{
    private readonly OwnerPolicyEvaluationTracker _tracker;

    public OwnerAuthorizationWriteGuard(OwnerPolicyEvaluationTracker tracker)
    {
        _tracker = tracker;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnsureAuthorized();
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        EnsureAuthorized();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        return ValueTask.FromResult(result);
    }

    private void EnsureAuthorized()
    {
        if (_tracker.IsViolated)
        {
            throw new InvalidOperationException(
                "An IOwnerAuthorizedMutation attempted to write before consulting IOwnerOnlyPolicy.Authorize. " +
                "Load the aggregate, authorize its owner, then mutate it.");
        }
    }
}
