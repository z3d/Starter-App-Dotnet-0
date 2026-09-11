using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StarterApp.Api.Infrastructure.Identity;

// Blocks the write, not just the response. OwnerAuthorizationBehavior runs after the handler, by
// which time SaveChanges has already committed, so on its own it can only report a missing owner
// check. This interceptor runs before SaveChanges and before any ExecuteUpdate/ExecuteDelete
// command, which bypass SaveChanges, and throws if the request was flagged as an owner-authorized
// mutation and IOwnerOnlyPolicy.Authorize has not run. Reads are never blocked: a handler loads the
// aggregate first and authorizes against what it loaded. Registered scoped so it sees the request's
// tracker; the outbox processor and the migrator run in scopes where nothing is flagged.
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
