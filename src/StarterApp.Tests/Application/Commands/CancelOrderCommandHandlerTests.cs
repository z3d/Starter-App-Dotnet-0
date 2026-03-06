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
}
