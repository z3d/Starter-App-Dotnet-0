using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;
using System.Text.Json;

namespace StarterApp.Tests.Application.Commands;

public class CreateOrderCommandHandlerTests
{
    [Fact]
    public void CreateOrderCommand_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var command = new CreateOrderCommand
        {
            CustomerId = 1,
            Items = new List<CreateOrderItemCommand>
            {
                new()
                {
                    ProductId = 1,
                    Quantity = 2
                }
            }
        };

        var validationContext = new ValidationContext(command);
        List<ValidationResult> validationResults = [];

        // Act
        var isValid = Validator.TryValidateObject(command, validationContext, validationResults, true);

        // Assert
        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void CreateOrderCommand_PropertiesTest()
    {
        // Arrange
        var command = new CreateOrderCommand
        {
            CustomerId = 123,
            Items = new List<CreateOrderItemCommand>
            {
                new()
                {
                    ProductId = 456,
                    Quantity = 3
                }
            }
        };

        // Act & Assert - Verify all properties are set correctly
        Assert.Equal(123, command.CustomerId);
        Assert.Single(command.Items);
        Assert.Equal(456, command.Items[0].ProductId);
        Assert.Equal(3, command.Items[0].Quantity);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateOrderAndReturnDto()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        // Create test customer and product first
        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Test Description", Money.Create(10.99m, "USD"), 100);

        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(context);

        var command = new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = new List<CreateOrderItemCommand>
            {
                new()
                {
                    ProductId = product.Id,
                    Quantity = 2
                }
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.CustomerId, result.CustomerId);
        Assert.Single(result.Items);
        Assert.Equal(command.Items[0].ProductId, result.Items[0].ProductId);
        Assert.Equal(command.Items[0].Quantity, result.Items[0].Quantity);
        Assert.True(result.Id > 0);
        Assert.Equal(product.Price.Amount, result.Items[0].UnitPriceExcludingGst);
        Assert.Equal(product.Price.Currency, result.Items[0].Currency);
        Assert.Equal(OrderItem.DefaultGstRate, result.Items[0].GstRate);

        // Verify the order was actually saved to the database
        var savedOrder = await context.Orders.FirstOrDefaultAsync(o => o.Id == result.Id);
        Assert.NotNull(savedOrder);
        Assert.Equal(command.CustomerId, savedOrder.CustomerId);

        // Check that order items were created (they're in a separate table)
        var orderItemsCount = await context.Set<OrderItem>().CountAsync(oi => oi.OrderId == result.Id);
        Assert.Equal(command.Items.Count, orderItemsCount);
    }

    [Fact]
    public async Task Handle_WithInsufficientStock_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Low Stock Product", "Description", Money.Create(10.00m, "USD"), 1);

        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(context);

        var command = new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items =
            [
                new() { ProductId = product.Id, Quantity = 5 }
            ]
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(command, CancellationToken.None));
        Assert.Contains("Insufficient stock", ex.Message);
        Assert.Contains("Available stock changed before the order could be placed", ex.Message);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldDecrementProductStock()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Description", Money.Create(10.00m, "USD"), 50);

        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(context);

        var command = new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items =
            [
                new() { ProductId = product.Id, Quantity = 8 }
            ]
        };

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert — stock should be decremented from 50 to 42
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(42, updatedProduct!.Stock);
    }

    [Fact]
    public async Task Handle_WithSecondProductInsufficientStock_ShouldNotDecrementFirstProductStock()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product1 = new Product("Product A", "Description", Money.Create(10.00m, "USD"), 100);
        var product2 = new Product("Product B", "Description", Money.Create(20.00m, "USD"), 2);

        context.Customers.Add(customer);
        context.Products.AddRange(product1, product2);
        await context.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(context);

        var command = new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items =
            [
                new() { ProductId = product1.Id, Quantity = 5 },
                new() { ProductId = product2.Id, Quantity = 10 }
            ]
        };

        // Act — second item should fail stock check
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(command, CancellationToken.None));

        // Assert — first product's stock should be unchanged (SaveChanges never called)
        await using var verifyContext = new ApplicationDbContext(options);
        var p1 = await verifyContext.Products.FindAsync(product1.Id);
        Assert.Equal(100, p1!.Stock);
    }

    [Fact]
    public async Task Handle_ShouldUseCatalogValuesForOrderItems()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Description", Money.Create(24.50m, "AUD"), 10);

        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(context);
        var command = new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items =
            [
                new()
                {
                    ProductId = product.Id,
                    Quantity = 2
                }
            ]
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        var item = Assert.Single(result.Items);
        Assert.Equal(24.50m, item.UnitPriceExcludingGst);
        Assert.Equal("AUD", item.Currency);
        Assert.Equal(OrderItem.DefaultGstRate, item.GstRate);
    }

    [Fact]
    public async Task Handle_ShouldPersistOrderCreatedOutboxMessage()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Description", Money.Create(19.99m, "USD"), 20);

        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(context);

        // Act
        var result = await handler.HandleAsync(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 2 }]
        }, CancellationToken.None);

        // Assert
        var outboxMessage = await context.OutboxMessages.SingleAsync();
        Assert.Equal("OrderCreatedDomainEvent", outboxMessage.Type);

        using var payload = JsonDocument.Parse(outboxMessage.Payload);
        Assert.Equal(result.Id, payload.RootElement.GetProperty("OrderId").GetInt32());
        Assert.Equal(customer.Id, payload.RootElement.GetProperty("CustomerId").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("LineItemCount").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("TotalQuantity").GetInt32());
        Assert.Equal("Pending", payload.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task Handle_WithDuplicateProductIds_ShouldThrowValidationException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Test Customer", Email.Create("test@example.com"));
        var product = new Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);

        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(context);
        var command = new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items =
            [
                new() { ProductId = product.Id, Quantity = 1 },
                new() { ProductId = product.Id, Quantity = 2 }
            ]
        };

        // Act & Assert
        await Assert.ThrowsAsync<StarterApp.Api.Infrastructure.Validation.ValidationException>(
            () => handler.HandleAsync(command, CancellationToken.None));
    }
}
