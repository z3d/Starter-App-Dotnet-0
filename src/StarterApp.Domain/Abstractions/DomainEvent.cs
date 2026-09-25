namespace StarterApp.Domain.Abstractions;

// OccurredOnUtc is unset until DomainEventsInterceptor stamps it at save.
public abstract class DomainEvent : IDomainEvent
{
    public abstract string EventType { get; }

    public DateTimeOffset OccurredOnUtc { get; internal set; }
}
