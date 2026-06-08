namespace StarterApp.Tests.TestBuilders;

public class CreateOrderCommandBuilder
{
    private int _customerId = 1;
    private readonly List<CreateOrderItemCommand> _items = [];

    public static CreateOrderCommandBuilder Default() => new();

    public CreateOrderCommandBuilder WithCustomerId(int customerId)
    {
        _customerId = customerId;
        return this;
    }

    public CreateOrderCommandBuilder WithItem(int productId, int quantity)
    {
        _items.Add(new CreateOrderItemCommand
        {
            ProductId = productId,
            Quantity = quantity
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
        new CreateOrderCommandBuilder()
            .WithCustomerId(customerId)
            .WithItem(productId, 2)
            .Build();

    public static CreateOrderCommand MultipleItemsOrder(int customerId = 1, int productId1 = 1, int productId2 = 2) =>
        new CreateOrderCommandBuilder()
            .WithCustomerId(customerId)
            .WithItem(productId1, 1)
            .WithItem(productId2, 3)
            .Build();
}


