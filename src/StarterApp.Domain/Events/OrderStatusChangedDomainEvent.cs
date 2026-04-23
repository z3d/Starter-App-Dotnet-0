using StarterApp.Domain.Entities;

namespace StarterApp.Domain.Events;

public sealed class OrderStatusChangedDomainEvent : IDomainEvent
{
    public const string Contract = "order.status-changed.v1";

    public OrderStatusChangedDomainEvent(Order order, OrderStatus previousStatus, OrderStatus newStatus)
    {
        ArgumentNullException.ThrowIfNull(order);

        OrderId = order.Id;
        CustomerId = order.CustomerId;
        PreviousStatus = previousStatus.ToString();
        NewStatus = newStatus.ToString();
        LastUpdated = order.LastUpdated;
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    public string EventType => Contract;
    public Guid OrderId { get; }
    public int CustomerId { get; }
    public string PreviousStatus { get; }
    public string NewStatus { get; }
    public DateTimeOffset LastUpdated { get; }
    public DateTimeOffset OccurredOnUtc { get; }
}
