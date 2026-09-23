namespace StarterApp.Domain.Abstractions;

// Base for events the interceptor stamps. OccurredOnUtc is default until SaveChanges runs; the
// setter is internal so only DomainEventsInterceptor (via AggregateRoot.StampDomainEvents) writes it.
public abstract class DomainEvent : IDomainEvent
{
    public abstract string EventType { get; }

    public DateTimeOffset OccurredOnUtc { get; internal set; }
}
