using StarterApp.Domain.Entities;

namespace StarterApp.Domain.Events;

public sealed class OrderStatusChangedDomainEvent : IDomainEvent
{
    public OrderStatusChangedDomainEvent(Order order, OrderStatus previousStatus, OrderStatus newStatus)
    {
        ArgumentNullException.ThrowIfNull(order);

        OrderId = order.Id;
        CustomerId = order.CustomerId;
        PreviousStatus = previousStatus.ToString();
        NewStatus = newStatus.ToString();
        LastUpdated = order.LastUpdated;
        OccurredOnUtc = DateTime.UtcNow;
    }

    public int OrderId { get; }
    public int CustomerId { get; }
    public string PreviousStatus { get; }
    public string NewStatus { get; }
    public DateTime LastUpdated { get; }
    public DateTime OccurredOnUtc { get; }
}
