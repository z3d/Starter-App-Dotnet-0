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

    /// <summary>
    /// Called by the DbContext AFTER the first SaveChanges, when database-assigned
    /// identity values (IDENTITY columns) are populated. Override to raise creation
    /// events that need the final entity key. Default is no-op.
    /// </summary>
    internal virtual void RecordCreation()
    {
    }
}
