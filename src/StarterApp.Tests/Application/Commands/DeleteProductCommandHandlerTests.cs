using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class DeleteProductCommandHandlerTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

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
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var product = new Product("Test Product", "Description", Money.Create(10.00m, "USD"), 50);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new DeleteProductCommandHandler(context);
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
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var handler = new DeleteProductCommandHandler(context);
        var command = new DeleteProductCommand(999);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithProductReferencedByOrder_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Create an order referencing the product
        var createHandler = new CreateOrderCommandHandler(context);
        await createHandler.HandleAsync(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1, UnitPriceExcludingGst = 10.00m }]
        }, CancellationToken.None);

        var handler = new DeleteProductCommandHandler(context);
        var command = new DeleteProductCommand(product.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
        Assert.Contains("existing orders", ex.Message);
    }
}
