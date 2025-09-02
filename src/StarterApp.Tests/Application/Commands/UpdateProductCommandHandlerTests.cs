using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class UpdateProductCommandHandlerTests
{
    [Fact]
    public void UpdateProductCommand_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var command = new UpdateProductCommand
        {
            Id = 1,
            Name = "Updated Product",
            Description = "Updated Description",
            Price = 15.99m,
            Currency = "USD",
            Stock = 50
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
    public void UpdateProductCommand_PropertiesTest()
    {
        // Arrange
        var command = new UpdateProductCommand
        {
            Id = 123,
            Name = "Updated Product",
            Description = "Updated Description",
            Price = 25.99m,
            Currency = "EUR",
            Stock = 75
        };

        // Act & Assert - Verify all properties are set correctly
        Assert.Equal(123, command.Id);
        Assert.Equal("Updated Product", command.Name);
        Assert.Equal("Updated Description", command.Description);
        Assert.Equal(25.99m, command.Price);
        Assert.Equal("EUR", command.Currency);
        Assert.Equal(75, command.Stock);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldUpdateProductAndReturnDto()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        // Create test product first
        var originalProduct = new Product("Original Product", "Original Description", Money.Create(10.99m, "USD"), 100);
        context.Products.Add(originalProduct);
        await context.SaveChangesAsync();

        var handler = new UpdateProductCommandHandler(context);

        var command = new UpdateProductCommand
        {
            Id = originalProduct.Id,
            Name = "Updated Product",
            Description = "Updated Description",
            Price = 15.99m,
            Currency = "USD",
            Stock = 50
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.Id, result.Id);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Description, result.Description);
        Assert.Equal(command.Price, result.Price);
        Assert.Equal(command.Currency, result.Currency);
        Assert.Equal(command.Stock, result.Stock);

        // Verify the product was actually updated in the database
        var updatedProduct = await context.Products.FindAsync(command.Id);
        Assert.NotNull(updatedProduct);
        Assert.Equal(command.Name, updatedProduct.Name);
        Assert.Equal(command.Description, updatedProduct.Description);
        Assert.Equal(command.Price, updatedProduct.Price.Amount);
        Assert.Equal(command.Currency, updatedProduct.Price.Currency);
        Assert.Equal(command.Stock, updatedProduct.Stock);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNull()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var handler = new UpdateProductCommandHandler(context);

        var command = new UpdateProductCommand
        {
            Id = 99999, // Non-existent ID
            Name = "Updated Product",
            Description = "Updated Description",
            Price = 15.99m,
            Currency = "USD",
            Stock = 50
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }
}
