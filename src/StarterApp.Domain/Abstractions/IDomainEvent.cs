namespace StarterApp.Domain.Abstractions;

public interface IDomainEvent
{
    // Subscriptions and old outbox rows are keyed on this name. Renaming the class must not change it.
    string EventType { get; }

    DateTimeOffset OccurredOnUtc { get; }
}
