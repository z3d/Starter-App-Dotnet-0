namespace StarterApp.Domain.Abstractions;

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    internal void ClearDomainEvents() => _domainEvents.Clear();

    // The domain never reads a clock. DomainEventsInterceptor stamps every pending event with the
    // one instant it also writes into the aggregates' audit columns, so an event and the row it
    // describes never disagree about when the change happened.
    internal void StampDomainEvents(DateTimeOffset occurredOnUtc)
    {
        foreach (var domainEvent in _domainEvents)
            if (domainEvent is DomainEvent stamped)
                stamped.OccurredOnUtc = occurredOnUtc;
    }

    // Called by the DbContext before SaveChanges so creation events share the same unit of work.
    internal virtual void RecordCreation()
    {
    }
}
