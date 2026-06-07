namespace StarterApp.Domain.Entities;

public class OrderItem
{
    public const decimal DefaultGstRate = 0.10m; // 10% GST

    public int Id { get; private set; }
    public Guid OrderId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public Money UnitPriceExcludingGst { get; private set; } = null!;
    public decimal GstRate { get; private set; }

    protected OrderItem()
    {
        ProductName = string.Empty;
    }

    public OrderItem(Guid orderId, int productId, string productName, int quantity, Money unitPriceExcludingGst, decimal gstRate = DefaultGstRate)
        : this(productId, productName, quantity, unitPriceExcludingGst, gstRate)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId must not be Guid.Empty", nameof(orderId));
        OrderId = orderId;
    }

    // Used before the parent order is saved; EF Core fills OrderId through the FK relationship.
    internal OrderItem(int productId, string productName, int quantity, Money unitPriceExcludingGst, decimal gstRate = DefaultGstRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentNullException.ThrowIfNull(unitPriceExcludingGst);
        ArgumentOutOfRangeException.ThrowIfNegative(gstRate);

        if (gstRate > 1.0m)
            throw new ArgumentOutOfRangeException(nameof(gstRate), gstRate,
                "GST rate must be a decimal value between 0 and 1 (e.g., 0.10 for 10%). Database constraint: DECIMAL(5,4) with max value 9.9999.");

        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPriceExcludingGst = unitPriceExcludingGst;
        GstRate = gstRate;
    }

    public Money GetUnitPriceIncludingGst()
    {
        return Money.Create(UnitPriceExcludingGst.Amount + GetUnitGstAmount(), UnitPriceExcludingGst.Currency);
    }

    public Money GetTotalPriceExcludingGst()
    {
        return Money.Create(UnitPriceExcludingGst.Amount * Quantity, UnitPriceExcludingGst.Currency);
    }

    public Money GetTotalPriceIncludingGst()
    {
        return Money.Create((UnitPriceExcludingGst.Amount + GetUnitGstAmount()) * Quantity, UnitPriceExcludingGst.Currency);
    }

    public Money GetTotalGstAmount()
    {
        return Money.Create(GetUnitGstAmount() * Quantity, UnitPriceExcludingGst.Currency);
    }

    // GST is rounded to whole cents PER UNIT, then multiplied by quantity, so per-unit and per-line
    // figures stay internally consistent: total GST == unit GST x qty, and total incl == total excl +
    // total GST. Rounding the line total instead can diverge from unit x qty by a cent at scale.
    private decimal GetUnitGstAmount()
    {
        return decimal.Round(UnitPriceExcludingGst.Amount * GstRate, Money.CurrencyDecimalPlaces, MidpointRounding.AwayFromZero);
    }

}


