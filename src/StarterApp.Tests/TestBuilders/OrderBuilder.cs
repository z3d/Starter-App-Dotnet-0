namespace StarterApp.Tests.TestBuilders;

public class OrderBuilder
{
    private int _customerId = 1;
    private readonly List<CreateOrderItemCommand> _items = [];

    public static OrderBuilder Default() => new();

    public OrderBuilder WithCustomerId(int customerId)
    {
        _customerId = customerId;
        return this;
    }

    public OrderBuilder WithItem(int productId, int quantity, decimal unitPriceExcludingGst, string currency = "USD", decimal gstRate = 0.10m)
    {
        _items.Add(new CreateOrderItemCommand
        {
            ProductId = productId,
            Quantity = quantity,
            UnitPriceExcludingGst = unitPriceExcludingGst,
            Currency = currency,
            GstRate = gstRate
        });
        return this;
    }

    public CreateOrderCommand Build()
    {
        return new CreateOrderCommand
        {
            CustomerId = _customerId,
            Items = _items
        };
    }

    public static CreateOrderCommand SimpleOrder(int customerId = 1, int productId = 1) =>
        new OrderBuilder()
            .WithCustomerId(customerId)
            .WithItem(productId, 2, 19.99m)
            .Build();

    public static CreateOrderCommand MultipleItemsOrder(int customerId = 1, int productId1 = 1, int productId2 = 2) =>
        new OrderBuilder()
            .WithCustomerId(customerId)
            .WithItem(productId1, 1, 10.00m)
            .WithItem(productId2, 3, 25.00m)
            .Build();
}