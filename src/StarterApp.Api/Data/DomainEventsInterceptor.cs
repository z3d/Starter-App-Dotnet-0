using Microsoft.EntityFrameworkCore.Diagnostics;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data;

// Single-SaveChanges outbox pattern, as a SaveChangesInterceptor.
//
// This lives in an interceptor (attached by AddPersistence) rather than ApplicationDbContext overrides so
// the context owns persistence only and the outbox-capture concern is a composable, separately-testable
// seam — the same interceptor wires unchanged onto any future DbContext. A context constructed without it
// (design-time tooling, model-only tests) simply does not capture. EF invokes SavingChanges once per
// SaveChanges call, outside the retrying execution strategy, so EnableRetryOnFailure cannot re-enter the
// capture and duplicate outbox rows.
//
// Aggregates raising creation events (via RecordCreation override) must assign their Id
// client-side (e.g. Guid.CreateVersion7) so the event payload can be built BEFORE SaveChanges.
// Enforced by DomainConventionTests.AggregatesOverridingRecordCreation_MustHaveGuidId.
//
// Why single-SaveChanges matters: EF's retrying execution strategy (EnableRetryOnFailure) rejects
// user-initiated transactions, and wrapping a two-SaveChanges flow in
// CreateExecutionStrategy().ExecuteAsync is unsafe — mid-flow retry leaves the ChangeTracker out of
// sync with the rolled-back DB. Capturing outbox rows into the same save lets EF manage its own
// retry-aware transaction.
public sealed class DomainEventsInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureDomainEventsIntoOutbox(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        CaptureDomainEventsIntoOutbox(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void CaptureDomainEventsIntoOutbox(DbContext? context)
    {
        if (context is null)
            return;

        var newAggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        foreach (var aggregate in newAggregates)
            aggregate.RecordCreation();

        var aggregatesWithEvents = context.ChangeTracker.Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        if (aggregatesWithEvents.Count == 0)
            return;

        var outboxMessages = aggregatesWithEvents
            .SelectMany(aggregate => aggregate.DomainEvents)
            .Select(OutboxMessage.Create)
            .ToList();

        if (outboxMessages.Count > 0)
            context.Set<OutboxMessage>().AddRange(outboxMessages);

        // Clear now — if SaveChanges throws, the caller retries at the use-case layer; a second pass
        // would otherwise duplicate the outbox rows. Aggregate state is still dirty in the tracker.
        foreach (var aggregate in aggregatesWithEvents)
            aggregate.ClearDomainEvents();
    }
}
