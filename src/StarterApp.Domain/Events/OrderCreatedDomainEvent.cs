using StarterApp.Domain.Entities;

namespace StarterApp.Domain.Events;

public sealed class OrderCreatedDomainEvent : IDomainEvent
{
    public const string Contract = "order.created.v1";

    public OrderCreatedDomainEvent(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        OrderId = order.Id;
        CustomerId = order.CustomerId;
        Status = order.Status.ToString();
        LineItemCount = order.Items.Count;
        TotalQuantity = order.Items.Sum(item => item.Quantity);
        TotalExcludingGst = order.GetTotalExcludingGst().Amount;
        TotalIncludingGst = order.GetTotalIncludingGst().Amount;
        TotalGstAmount = order.GetTotalGstAmount().Amount;
        Currency = order.Items.FirstOrDefault()?.UnitPriceExcludingGst.Currency ?? "USD";
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    public string EventType => Contract;
    public int OrderId { get; }
    public int CustomerId { get; }
    public string Status { get; }
    public int LineItemCount { get; }
    public int TotalQuantity { get; }
    public decimal TotalExcludingGst { get; }
    public decimal TotalIncludingGst { get; }
    public decimal TotalGstAmount { get; }
    public string Currency { get; }
    public DateTimeOffset OccurredOnUtc { get; }
}
