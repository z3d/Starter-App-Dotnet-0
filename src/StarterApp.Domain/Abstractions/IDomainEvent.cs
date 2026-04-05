namespace StarterApp.Domain.Abstractions;

public interface IDomainEvent
{
    /// <summary>
    /// Stable, versioned contract name for event routing (e.g. "order.created.v1").
    /// Decoupled from CLR type names so class renames do not break Service Bus subscriptions
    /// or already-persisted outbox rows.
    /// </summary>
    string EventType { get; }

    DateTimeOffset OccurredOnUtc { get; }
}
