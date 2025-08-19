namespace StarterApp.Domain.Entities;

public class OrderItem
{
    public const decimal DefaultGstRate = 0.10m; // 10% GST

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public Money UnitPriceExcludingGst { get; private set; } = null!;
    public decimal GstRate { get; private set; }

    protected OrderItem() 
    {
        ProductName = string.Empty;
    }

    public OrderItem(int orderId, int productId, string productName, int quantity, Money unitPriceExcludingGst, decimal gstRate = DefaultGstRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentNullException.ThrowIfNull(unitPriceExcludingGst);
        ArgumentOutOfRangeException.ThrowIfNegative(gstRate);

        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPriceExcludingGst = unitPriceExcludingGst;
        GstRate = gstRate;
    }

    public Money GetUnitPriceIncludingGst()
    {
        var gstAmount = UnitPriceExcludingGst.Amount * GstRate;
        var totalAmount = UnitPriceExcludingGst.Amount + gstAmount;
        return Money.Create(totalAmount, UnitPriceExcludingGst.Currency);
    }

    public Money GetTotalPriceExcludingGst()
    {
        var totalAmount = UnitPriceExcludingGst.Amount * Quantity;
        return Money.Create(totalAmount, UnitPriceExcludingGst.Currency);
    }

    public Money GetTotalPriceIncludingGst()
    {
        var unitPriceIncGst = GetUnitPriceIncludingGst();
        var totalAmount = unitPriceIncGst.Amount * Quantity;
        return Money.Create(totalAmount, UnitPriceExcludingGst.Currency);
    }

    public Money GetTotalGstAmount()
    {
        var gstPerUnit = UnitPriceExcludingGst.Amount * GstRate;
        var totalGst = gstPerUnit * Quantity;
        return Money.Create(totalGst, UnitPriceExcludingGst.Currency);
    }

    public void SetId(int id)
    {
        Id = id;
    }
}