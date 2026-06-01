
namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class DeleteProductCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public DeleteProductCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public void DeleteProductCommand_PropertiesTest()
    {
        // Arrange & Act
        var command = new DeleteProductCommand(42);

        // Assert
        Assert.Equal(42, command.Id);
    }

    [Fact]
    public async Task Handle_WithExistingProduct_ShouldDeleteProduct()
    {
        // Arrange
        await using var context = CreateContext();

        var product = new Product("Test Product", "Description", Money.Create(10.00m, "USD"), 50);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new DeleteProductCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new DeleteProductCommand(product.Id);

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        var deletedProduct = await context.Products.FindAsync(product.Id);
        Assert.Null(deletedProduct);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        await using var context = CreateContext();

        var handler = new DeleteProductCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new DeleteProductCommand(999);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithProductReferencedByOrder_ShouldThrowInvalidOperationException()
    {
        // Arrange
        await using var context = CreateContext();

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Create an order referencing the product
        var createHandler = new CreateOrderCommandHandler(context, TestOwnerOnlyPolicy.Instance);
        await createHandler.HandleAsync(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }]
        }, CancellationToken.None);

        var handler = new DeleteProductCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new DeleteProductCommand(product.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
        Assert.Contains("existing orders", ex.Message);
    }
}
