using Microsoft.EntityFrameworkCore.Diagnostics;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data;

// Without this interceptor a context writes no outbox rows and no audit stamps; EF calls it outside the retry strategy, so a retry cannot duplicate rows.
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

        // Cleared before the save: a retry at the use-case layer would otherwise duplicate the outbox rows.
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
