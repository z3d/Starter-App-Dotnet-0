using StarterApp.Domain.Entities;

namespace StarterApp.Domain.Events;

public sealed class OrderCreatedDomainEvent : IDomainEvent
{
    public OrderCreatedDomainEvent(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        Order = order;
        LineItemCount = order.Items.Count;
        TotalQuantity = order.Items.Sum(item => item.Quantity);
        TotalExcludingGst = order.GetTotalExcludingGst().Amount;
        TotalIncludingGst = order.GetTotalIncludingGst().Amount;
        TotalGstAmount = order.GetTotalGstAmount().Amount;
        Currency = order.Items.FirstOrDefault()?.UnitPriceExcludingGst.Currency ?? "USD";
        OccurredOnUtc = DateTime.UtcNow;
    }

    public Order Order { get; }
    public int LineItemCount { get; }
    public int TotalQuantity { get; }
    public decimal TotalExcludingGst { get; }
    public decimal TotalIncludingGst { get; }
    public decimal TotalGstAmount { get; }
    public string Currency { get; }
    public DateTime OccurredOnUtc { get; }
}
