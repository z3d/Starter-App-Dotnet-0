using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class UpdateOrderStatusCommandHandlerTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task Handle_ShouldUpdateStatusAndReturnDto()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var order = new Order(customer.Id);
        order.AddItem(1, "Product A", 1, Money.Create(10m, "USD"), 0.1m);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var handler = new UpdateOrderStatusCommandHandler(context);
        var command = new UpdateOrderStatusCommand { OrderId = order.Id, Status = "Confirmed" };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal("Confirmed", result.Status);
        Assert.Equal(order.Id, result.Id);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var handler = new UpdateOrderStatusCommandHandler(context);
        var command = new UpdateOrderStatusCommand { OrderId = 999, Status = "Confirmed" };

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithInvalidTransition_ShouldThrowInvalidOperationException()
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

        var handler = new UpdateOrderStatusCommandHandler(context);
        var command = new UpdateOrderStatusCommand { OrderId = order.Id, Status = "Delivered" };

        // Act & Assert — Pending → Delivered is not a valid transition
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }
}
