namespace StarterApp.Domain.Abstractions;

public interface IDomainEvent
{
    // Stable event routing contract; class renames must not break subscriptions or old outbox rows.
    string EventType { get; }

    DateTimeOffset OccurredOnUtc { get; }
}
