namespace StarterApp.SharedKernel.Abstractions;

// OccurredOnUtc stays unset until DomainEventsInterceptor fills it in when the row is saved.
public abstract class DomainEvent : IDomainEvent
{
    public abstract string EventType { get; }

    public DateTimeOffset OccurredOnUtc { get; internal set; }
}
