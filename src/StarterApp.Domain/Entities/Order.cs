using StarterApp.Domain.Events;

namespace StarterApp.Domain.Entities;

public class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; private set; }
    public int CustomerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyList<OrderItem> Items
    {
        get => _items.AsReadOnly();
        private set { } // Required by EF Core property detection; data loaded via _items backing field (see ApplicationDbContext HasMany config with PropertyAccessMode.Field)
    }
    public DateTimeOffset LastUpdated { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    protected Order()
    {
        OrderDate = DateTimeOffset.UtcNow;
        Status = OrderStatus.Pending;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    public Order(int customerId)
        : this(Guid.CreateVersion7(), customerId)
    {
    }

    internal Order(Guid id, int customerId)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Order id cannot be empty", nameof(id));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);

        // Client-assigned Id is required so creation events can be built before SaveChanges —
        // this is what keeps outbox capture inside a single SaveChanges and makes
        // EnableRetryOnFailure safe. Guid v7 is time-ordered, preserving insert locality.
        Id = id;
        CustomerId = customerId;
        OrderDate = DateTimeOffset.UtcNow;
        Status = OrderStatus.Pending;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot add items to a non-pending order");

        EnsureCurrencyMatchesExistingItems(item.UnitPriceExcludingGst.Currency);

        // Check if item with same product already exists
        var existingItem = _items.FirstOrDefault(i => i.ProductId == item.ProductId);
        if (existingItem != null)
        {
            // Remove the existing item and add the new one (replacing it)
            _items.Remove(existingItem);
        }

        _items.Add(item);
        LastUpdated = DateTimeOffset.UtcNow;
    }

    // EF Core sets the order FK on save, so callers do not need a persisted OrderId yet.
    public OrderItem AddItem(int productId, string productName, int quantity, Money unitPrice, decimal gstRate = OrderItem.DefaultGstRate)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot add items to a non-pending order");

        ArgumentNullException.ThrowIfNull(unitPrice);

        EnsureCurrencyMatchesExistingItems(unitPrice.Currency);

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
            _items.Remove(existingItem);

        var item = new OrderItem(productId, productName, quantity, unitPrice, gstRate);
        _items.Add(item);
        LastUpdated = DateTimeOffset.UtcNow;
        return item;
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
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }

    public void UpdateStatus(OrderStatus newStatus)
    {
        if (!IsValidStatusTransition(Status, newStatus))
            throw new InvalidOperationException($"Cannot transition from {Status} to {newStatus}");

        var previousStatus = Status;
        Status = newStatus;
        LastUpdated = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new OrderStatusChangedDomainEvent(this, previousStatus, newStatus));
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

    internal override void RecordCreation()
    {
        RaiseDomainEvent(new OrderCreatedDomainEvent(this));
    }

    private void EnsureCurrencyMatchesExistingItems(string currency)
    {
        if (_items.Count == 0)
            return;

        var existingCurrency = _items[0].UnitPriceExcludingGst.Currency;
        if (!string.Equals(existingCurrency, currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("All order items must use the same currency");
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

    internal static Order Reconstitute(Guid id, int customerId, DateTimeOffset orderDate, OrderStatus status, DateTimeOffset lastUpdated, List<OrderItem> items)
    {
        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            OrderDate = orderDate,
            Status = status,
            LastUpdated = lastUpdated
        };
        order._items.AddRange(items);
        return order;
    }
}
