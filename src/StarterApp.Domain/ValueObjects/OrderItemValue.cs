namespace StarterApp.Domain.ValueObjects;

public class OrderItemValue
{
    public const decimal DefaultGstRate = 0.10m; // 10% GST

    public int ProductId { get; private set; }
    public string ProductName { get; private set; }
    public int Quantity { get; private set; }
    public Money UnitPriceExcludingGst { get; private set; }
    public decimal GstRate { get; private set; }

    private OrderItemValue(int productId, string productName, int quantity, Money unitPriceExcludingGst, decimal gstRate)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPriceExcludingGst = unitPriceExcludingGst;
        GstRate = gstRate;
    }

    public static OrderItemValue Create(int productId, string productName, int quantity, Money unitPriceExcludingGst, decimal gstRate = DefaultGstRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentNullException.ThrowIfNull(unitPriceExcludingGst);
        ArgumentOutOfRangeException.ThrowIfNegative(gstRate);

        return new OrderItemValue(productId, productName, quantity, unitPriceExcludingGst, gstRate);
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

    public override bool Equals(object? obj)
    {
        if (obj is not OrderItemValue other)
            return false;

        return ProductId == other.ProductId && 
               ProductName == other.ProductName && 
               Quantity == other.Quantity && 
               UnitPriceExcludingGst.Equals(other.UnitPriceExcludingGst) && 
               GstRate == other.GstRate;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ProductId, ProductName, Quantity, UnitPriceExcludingGst, GstRate);
    }

    public override string ToString()
    {
        return $"{ProductName} x {Quantity} @ {UnitPriceExcludingGst} (excl GST)";
    }
}