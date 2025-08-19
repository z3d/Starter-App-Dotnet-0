namespace StarterApp.Domain.Entities;

/// <summary>
/// Entity representation of OrderItem for EF Core mapping
/// </summary>
public class OrderItem
{
    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPriceExcludingGst { get; private set; }
    public string Currency { get; private set; } = "USD";
    public decimal GstRate { get; private set; }

    protected OrderItem() { }

    public OrderItem(int orderId, OrderItemValue orderItem)
    {
        OrderId = orderId;
        ProductId = orderItem.ProductId;
        ProductName = orderItem.ProductName;
        Quantity = orderItem.Quantity;
        UnitPriceExcludingGst = orderItem.UnitPriceExcludingGst.Amount;
        Currency = orderItem.UnitPriceExcludingGst.Currency;
        GstRate = orderItem.GstRate;
    }

    public OrderItemValue ToValueObject()
    {
        return OrderItemValue.Create(
            ProductId,
            ProductName,
            Quantity,
            Money.Create(UnitPriceExcludingGst, Currency),
            GstRate
        );
    }

    public void SetId(int id)
    {
        Id = id;
    }
}