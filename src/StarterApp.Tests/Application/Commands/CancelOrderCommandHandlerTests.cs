
namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class CancelOrderCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public CancelOrderCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_ShouldCancelOrderAndReturnDto()
    {
        // Arrange
        await using var context = CreateContext();

        var customer = TestEntities.Customer("Test Customer", Email.Create("test@example.com"));
        context.Customers.Add(customer);
        var product = TestEntities.Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var order = TestEntities.Order(customer.Id);
        order.AddItem(product.Id, product.Name, 1, Money.Create(10.00m, "USD"), 0.1m);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var handler = new CancelOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new CancelOrderCommand { OrderId = order.Id };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(order.Id, result.Id);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldThrowEntityNotFoundException()
    {
        // Arrange
        await using var context = CreateContext();

        var handler = new CancelOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new CancelOrderCommand { OrderId = Guid.NewGuid() };

        // Act & Assert
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldRestoreProductStock()
    {
        // Arrange
        await using var context = CreateContext();

        var customer = TestEntities.Customer("Test Customer", Email.Create("test@example.com"));
        var product = TestEntities.Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Create order (which decrements stock)
        var createHandler = new CreateOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var createCommand = new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items =
            [
                new() { ProductId = product.Id, Quantity = 15 }
            ]
        };
        var orderDto = await createHandler.HandleAsync(createCommand, CancellationToken.None);

        // Verify stock was decremented
        var decrementedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(85, decrementedProduct!.Stock);

        // Act — cancel the order
        var cancelHandler = new CancelOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        await cancelHandler.HandleAsync(new CancelOrderCommand { OrderId = orderDto.Id }, CancellationToken.None);

        // Assert — stock should be restored to 100
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(100, updatedProduct!.Stock);
    }

    [Fact]
    public async Task Handle_ShouldInvalidateProductCache_AfterRestoringStock()
    {
        // Arrange
        await using var context = CreateContext();

        var customer = TestEntities.Customer("Test Customer", Email.Create("test@example.com"));
        var product = TestEntities.Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var createHandler = new CreateOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var orderDto = await createHandler.HandleAsync(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 5 }]
        }, CancellationToken.None);

        var recordingInvalidator = new RecordingCacheInvalidator();
        var cancelHandler = new CancelOrderCommandHandler(context, recordingInvalidator, TestOwnerOnlyPolicy.Instance);

        // Act
        await cancelHandler.HandleAsync(new CancelOrderCommand { OrderId = orderDto.Id }, CancellationToken.None);

        // Assert — cancelling restores stock, so the cached product read model must be purged
        Assert.Contains(product.Id, recordingInvalidator.InvalidatedProductIds);
    }

    [Fact]
    public async Task Handle_ShouldPersistCancelledStatusOutboxMessage()
    {
        // Arrange
        await using var context = CreateContext();

        var customer = TestEntities.Customer("Test Customer", Email.Create("test@example.com"));
        var product = TestEntities.Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var createHandler = new CreateOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var createdOrder = await createHandler.HandleAsync(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }]
        }, CancellationToken.None);

        var cancelHandler = new CancelOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);

        // Act
        await cancelHandler.HandleAsync(new CancelOrderCommand { OrderId = createdOrder.Id }, CancellationToken.None);

        // Assert
        var statusChangedMessage = await context.OutboxMessages
            .Where(message => message.Type == OrderStatusChangedDomainEvent.Contract)
            .SingleAsync();

        using var payload = JsonDocument.Parse(statusChangedMessage.Payload);
        Assert.Equal("Pending", payload.RootElement.GetProperty("PreviousStatus").GetString());
        Assert.Equal("Cancelled", payload.RootElement.GetProperty("NewStatus").GetString());
    }
}
