using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StarterApp.Api.Infrastructure.Identity;

// Runs before SaveChanges and before ExecuteUpdate and ExecuteDelete, which the behaviour cannot see on its own. Reads are never blocked.
internal sealed class OwnerAuthorizationWriteGuard : DbCommandInterceptor, ISaveChangesInterceptor
{
    private readonly OwnerPolicyEvaluationTracker _tracker;
    private readonly ICurrentUser? _currentUser;

    public OwnerAuthorizationWriteGuard(OwnerPolicyEvaluationTracker tracker, ICurrentUser? currentUser = null)
    {
        _tracker = tracker;
        _currentUser = currentUser;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnsureAuthorized();
        EnsureEveryChangedRowIsTheCallers(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        EnsureEveryChangedRowIsTheCallers(eventData.Context);
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

    // One owner check marks the whole request, so each row it changes is checked as well: authorizing one aggregate does not let the handler change another's.
    private void EnsureEveryChangedRowIsTheCallers(DbContext? context)
    {
        if (context is null || !_tracker.RequiresEvaluation)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var owner = entry.Metadata.FindProperty("OwnerSubject");
            var tenant = entry.Metadata.FindProperty("TenantId");
            if (owner is null || tenant is null)
                continue;

            var before = entry.State == EntityState.Added || IsTheCallers(entry.OriginalValues[owner], entry.OriginalValues[tenant]);
            var after = entry.State == EntityState.Deleted || IsTheCallers(entry.CurrentValues[owner], entry.CurrentValues[tenant]);
            if (before && after)
                continue;

            throw new InvalidOperationException(
                $"An IOwnerAuthorizedMutation tried to change a {entry.Metadata.ClrType.Name} the caller does not own. " +
                "Authorize each aggregate the handler changes, not one of them.");
        }
    }

    private bool IsTheCallers(object? ownerSubject, object? tenantId) =>
        _currentUser is { IsAuthenticated: true } caller &&
        string.Equals(ownerSubject as string, caller.Subject, StringComparison.Ordinal) &&
        string.Equals(tenantId as string, caller.TenantId, StringComparison.Ordinal);

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
