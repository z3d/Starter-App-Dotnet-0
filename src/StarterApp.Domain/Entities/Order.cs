namespace StarterApp.Domain.Entities;

public class Order
{
    private readonly List<OrderItem> _items = [];

    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyList<OrderItem> Items { get; private set; } = [];
    public DateTime LastUpdated { get; private set; }

    protected Order()
    {
        OrderDate = DateTime.UtcNow;
        Status = OrderStatus.Pending;
        LastUpdated = DateTime.UtcNow;
        Items = _items.AsReadOnly();
    }

    public Order(int customerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);

        CustomerId = customerId;
        OrderDate = DateTime.UtcNow;
        Status = OrderStatus.Pending;
        LastUpdated = DateTime.UtcNow;
        Items = _items.AsReadOnly();
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot add items to a non-pending order");

        // Check if item with same product already exists
        var existingItem = _items.FirstOrDefault(i => i.ProductId == item.ProductId);
        if (existingItem != null)
        {
            // Remove the existing item and add the new one (replacing it)
            _items.Remove(existingItem);
        }

        _items.Add(item);
        Items = _items.AsReadOnly();
        LastUpdated = DateTime.UtcNow;
    }

    public void RemoveItem(int productId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);

        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot remove items from a non-pending order");

        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            _items.Remove(item);
            Items = _items.AsReadOnly();
            LastUpdated = DateTime.UtcNow;
        }
    }

    public void UpdateStatus(OrderStatus newStatus)
    {
        if (!IsValidStatusTransition(Status, newStatus))
            throw new InvalidOperationException($"Cannot transition from {Status} to {newStatus}");

        Status = newStatus;
        LastUpdated = DateTime.UtcNow;
    }

    public void Confirm()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("Cannot confirm an order with no items");

        UpdateStatus(OrderStatus.Confirmed);
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Delivered)
            throw new InvalidOperationException("Cannot cancel a delivered order");

        UpdateStatus(OrderStatus.Cancelled);
    }

    public Money GetTotalExcludingGst()
    {
        if (_items.Count == 0)
            return Money.Create(0);

        var firstCurrency = _items[0].UnitPriceExcludingGst.Currency;
        var total = _items.Sum(item => item.GetTotalPriceExcludingGst().Amount);
        return Money.Create(total, firstCurrency);
    }

    public Money GetTotalIncludingGst()
    {
        if (_items.Count == 0)
            return Money.Create(0);

        var firstCurrency = _items[0].UnitPriceExcludingGst.Currency;
        var total = _items.Sum(item => item.GetTotalPriceIncludingGst().Amount);
        return Money.Create(total, firstCurrency);
    }

    public Money GetTotalGstAmount()
    {
        if (_items.Count == 0)
            return Money.Create(0);

        var firstCurrency = _items[0].UnitPriceExcludingGst.Currency;
        var totalGst = _items.Sum(item => item.GetTotalGstAmount().Amount);
        return Money.Create(totalGst, firstCurrency);
    }

    private static bool IsValidStatusTransition(OrderStatus currentStatus, OrderStatus newStatus)
    {
        return currentStatus switch
        {
            OrderStatus.Pending => newStatus == OrderStatus.Confirmed || newStatus == OrderStatus.Cancelled,
            OrderStatus.Confirmed => newStatus == OrderStatus.Processing || newStatus == OrderStatus.Cancelled,
            OrderStatus.Processing => newStatus == OrderStatus.Shipped || newStatus == OrderStatus.Cancelled,
            OrderStatus.Shipped => newStatus == OrderStatus.Delivered,
            OrderStatus.Delivered => false, // Cannot change from delivered
            OrderStatus.Cancelled => false, // Cannot change from cancelled
            _ => false
        };
    }

    public void SetId(int id)
    {
        Id = id;
    }

    public void LoadFromDatabase(DateTime orderDate, OrderStatus status, DateTime lastUpdated, List<OrderItem> items)
    {
        OrderDate = orderDate;
        Status = status;
        LastUpdated = lastUpdated;
        
        _items.Clear();
        _items.AddRange(items);
        Items = _items.AsReadOnly();
    }
}