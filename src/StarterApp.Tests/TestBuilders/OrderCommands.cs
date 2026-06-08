namespace StarterApp.Tests.TestBuilders;

// Ready-made CreateOrderCommand scenarios for order API integration tests. Order setup is the one
// command shape worth a helper (it carries an item list and orders need prerequisite data), so the
// common scenarios live here. Customer/Product commands are simple object initializers written
// inline at their call sites.
internal static class OrderCommands
{
    public static CreateOrderCommand SimpleOrder(int customerId = 1, int productId = 1) =>
        new() { CustomerId = customerId, Items = [new CreateOrderItemCommand { ProductId = productId, Quantity = 2 }] };

    public static CreateOrderCommand MultipleItemsOrder(int customerId = 1, int productId1 = 1, int productId2 = 2) =>
        new()
        {
            CustomerId = customerId,
            Items =
            [
                new CreateOrderItemCommand { ProductId = productId1, Quantity = 1 },
                new CreateOrderItemCommand { ProductId = productId2, Quantity = 3 }
            ]
        };
}
