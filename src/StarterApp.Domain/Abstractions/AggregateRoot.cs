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

    internal void StampDomainEvents(DateTimeOffset occurredOnUtc)
    {
        foreach (var domainEvent in _domainEvents)
            if (domainEvent is DomainEvent stamped)
                stamped.OccurredOnUtc = occurredOnUtc;
    }

    internal virtual void RecordCreation()
    {
    }
}
