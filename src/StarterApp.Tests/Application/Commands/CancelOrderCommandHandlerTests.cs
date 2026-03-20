using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class CancelOrderCommandHandlerTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task Handle_ShouldCancelOrderAndReturnDto()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var order = new Order(customer.Id);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var handler = new CancelOrderCommandHandler(context);
        var command = new CancelOrderCommand { OrderId = order.Id };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(order.Id, result.Id);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var handler = new CancelOrderCommandHandler(context);
        var command = new CancelOrderCommand { OrderId = 999 };

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldRestoreProductStock()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Create order (which decrements stock)
        var createHandler = new CreateOrderCommandHandler(context);
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
        var cancelHandler = new CancelOrderCommandHandler(context);
        await cancelHandler.HandleAsync(new CancelOrderCommand { OrderId = orderDto.Id }, CancellationToken.None);

        // Assert — stock should be restored to 100
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(100, updatedProduct!.Stock);
    }
}
