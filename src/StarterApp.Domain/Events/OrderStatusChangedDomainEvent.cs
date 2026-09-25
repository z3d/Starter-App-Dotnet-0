using StarterApp.Domain.Entities;

namespace StarterApp.Domain.Events;

public sealed class OrderStatusChangedDomainEvent : DomainEvent
{
    public const string Contract = "order.status-changed.v1";

    public OrderStatusChangedDomainEvent(Order order, OrderStatus previousStatus, OrderStatus newStatus)
    {
        ArgumentNullException.ThrowIfNull(order);

        OrderId = order.Id;
        CustomerId = order.CustomerId;
        PreviousStatus = previousStatus.ToString();
        NewStatus = newStatus.ToString();
    }

    public override string EventType => Contract;
    public Guid OrderId { get; }
    public int CustomerId { get; }
    public string PreviousStatus { get; }
    public string NewStatus { get; }

    // Same instant as the row's LastUpdated: both come from one clock read at save.
    public DateTimeOffset LastUpdated => OccurredOnUtc;
}
