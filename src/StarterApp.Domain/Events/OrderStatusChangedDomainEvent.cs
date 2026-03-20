using StarterApp.Domain.Entities;

namespace StarterApp.Domain.Events;

public sealed class OrderStatusChangedDomainEvent : IDomainEvent
{
    public OrderStatusChangedDomainEvent(Order order, OrderStatus previousStatus, OrderStatus newStatus)
    {
        ArgumentNullException.ThrowIfNull(order);

        Order = order;
        PreviousStatus = previousStatus.ToString();
        NewStatus = newStatus.ToString();
        OccurredOnUtc = DateTime.UtcNow;
    }

    public Order Order { get; }
    public string PreviousStatus { get; }
    public string NewStatus { get; }
    public DateTime OccurredOnUtc { get; }
}
