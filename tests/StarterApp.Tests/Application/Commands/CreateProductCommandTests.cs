using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class CreateProductCommandTests
{
    [Fact]
    public void CreateProductCommandValidator_WithValidData_ShouldPassValidation()
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

        var validator = new CreateProductCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("12!")]
    [InlineData("US1")]
    public void CreateProductCommandValidator_WithInvalidCurrencyCode_ShouldReturnValidationError(string currency)
    {
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = currency,
            Stock = 100
        };

        var validator = new CreateProductCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Currency));
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
}
