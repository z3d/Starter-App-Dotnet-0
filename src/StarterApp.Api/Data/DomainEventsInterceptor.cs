using Microsoft.EntityFrameworkCore.Diagnostics;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data;

// A context built without this interceptor writes no outbox rows and leaves the audit columns unset.
// EF calls SavingChanges once per SaveChanges, outside the retrying execution strategy, so
// EnableRetryOnFailure cannot run the capture twice and duplicate outbox rows.
// The event payload is serialized before the save, so an aggregate that raises a creation event
// must assign its own Id. DomainConventionTests.AggregatesOverridingRecordCreation_MustHaveGuidId
// enforces that.
public sealed class DomainEventsInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
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

    private void CaptureDomainEventsIntoOutbox(DbContext? context)
    {
        if (context is null)
            return;

        var now = timeProvider.GetUtcNow();
        StampAuditColumns(context, now);

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

        foreach (var aggregate in aggregatesWithEvents)
            aggregate.StampDomainEvents(now);

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

    private static void StampAuditColumns(DbContext context, DateTimeOffset now)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("DateCreated") is not null)
                entry.Property("DateCreated").CurrentValue = now;

            if (entry.Metadata.FindProperty("LastUpdated") is not null)
                entry.Property("LastUpdated").CurrentValue = now;
        }
    }
}
