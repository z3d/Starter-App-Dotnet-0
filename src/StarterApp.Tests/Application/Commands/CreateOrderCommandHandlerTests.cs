using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

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
                    Quantity = 2,
                    UnitPriceExcludingGst = 10.99m,
                    Currency = "USD",
                    GstRate = 0.1m
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
                    Quantity = 3,
                    UnitPriceExcludingGst = 15.99m,
                    Currency = "USD",
                    GstRate = 0.15m
                }
            }
        };

        // Act & Assert - Verify all properties are set correctly
        Assert.Equal(123, command.CustomerId);
        Assert.Single(command.Items);
        Assert.Equal(456, command.Items[0].ProductId);
        Assert.Equal(3, command.Items[0].Quantity);
        Assert.Equal(15.99m, command.Items[0].UnitPriceExcludingGst);
        Assert.Equal("USD", command.Items[0].Currency);
        Assert.Equal(0.15m, command.Items[0].GstRate);
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
                    Quantity = 2,
                    UnitPriceExcludingGst = 10.99m,
                    Currency = "USD",
                    GstRate = 0.1m
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

        // Verify the order was actually saved to the database
        var savedOrder = await context.Orders.FirstOrDefaultAsync(o => o.Id == result.Id);
        Assert.NotNull(savedOrder);
        Assert.Equal(command.CustomerId, savedOrder.CustomerId);
        
        // Check that order items were created (they're in a separate table)
        var orderItemsCount = await context.Set<OrderItem>().CountAsync(oi => oi.OrderId == result.Id);
        Assert.Equal(command.Items.Count, orderItemsCount);
    }
}
