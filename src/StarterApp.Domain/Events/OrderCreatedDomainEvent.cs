using StarterApp.Domain.Entities;

namespace StarterApp.Domain.Events;

public sealed class OrderCreatedDomainEvent : DomainEvent
{
    public const string Contract = "order.created.v1";

    public OrderCreatedDomainEvent(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        OrderId = order.Id;
        CustomerId = order.CustomerId;
        TenantId = order.TenantId;
        OwnerSubject = order.OwnerSubject;
        Status = order.Status.ToString();
        LineItemCount = order.Items.Count;
        TotalQuantity = order.Items.Sum(item => item.Quantity);
        TotalExcludingGst = order.GetTotalExcludingGst().Amount;
        TotalIncludingGst = order.GetTotalIncludingGst().Amount;
        TotalGstAmount = order.GetTotalGstAmount().Amount;
        Currency = order.Items[0].UnitPriceExcludingGst.Currency;
    }

    public override string EventType => Contract;
    public Guid OrderId { get; }
    public int CustomerId { get; }
    // The scope the change was made in, so a subscriber acts in the same one; opaque ids, no personal detail.
    public string TenantId { get; }
    public string OwnerSubject { get; }
    public string Status { get; }
    public int LineItemCount { get; }
    public int TotalQuantity { get; }
    public decimal TotalExcludingGst { get; }
    public decimal TotalIncludingGst { get; }
    public decimal TotalGstAmount { get; }
    public string Currency { get; }
}
