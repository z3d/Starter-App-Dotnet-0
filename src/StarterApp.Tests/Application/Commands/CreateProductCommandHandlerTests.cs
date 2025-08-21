using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class CreateProductCommandHandlerTests
{
    [Fact]
    public void CreateProductCommand_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = "USD",
            Stock = 100
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
    public void CreateProductCommand_PropertiesTest()
    {
        // Arrange
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = "USD", 
            Stock = 100
        };

        // Act & Assert - Verify all properties are set correctly
        Assert.Equal("Test Product", command.Name);
        Assert.Equal("Test Description", command.Description);
        Assert.Equal(10.99m, command.Price);
        Assert.Equal("USD", command.Currency);
        Assert.Equal(100, command.Stock);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateProductAndReturnDto()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var handler = new CreateProductCommandHandler(context);

        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = "USD",
            Stock = 100
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Description, result.Description);
        Assert.Equal(command.Price, result.Price);
        Assert.Equal(command.Currency, result.Currency);
        Assert.Equal(command.Stock, result.Stock);
        Assert.True(result.Id > 0);

        // Verify the product was actually saved to the database
        var savedProduct = await context.Products.FirstOrDefaultAsync(p => p.Name == command.Name);
        Assert.NotNull(savedProduct);
        Assert.Equal(command.Name, savedProduct.Name);
        Assert.Equal(command.Description, savedProduct.Description);
        Assert.Equal(command.Price, savedProduct.Price.Amount);
        Assert.Equal(command.Currency, savedProduct.Price.Currency);
        Assert.Equal(command.Stock, savedProduct.Stock);
    }
}
