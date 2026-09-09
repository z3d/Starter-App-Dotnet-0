using Microsoft.EntityFrameworkCore.Diagnostics;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data;

// Capture domain events as outbox rows in the same SaveChanges as the aggregates.
// AddPersistence registers this interceptor; contexts without it do not capture events.
// Creation events need client-assigned IDs because payloads are serialized before saving.
// See DomainConventionTests.AggregatesOverridingRecordCreation_MustHaveGuidId.
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
