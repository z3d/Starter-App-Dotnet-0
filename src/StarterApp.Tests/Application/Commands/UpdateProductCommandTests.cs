using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class UpdateProductCommandTests
{
    [Fact]
    public void UpdateProductCommandValidator_WithValidData_ShouldPassValidation()
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

        var validator = new UpdateProductCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void UpdateProductCommandValidator_WithMissingPriceAndStock_ShouldReturnValidationErrors()
    {
        var command = new UpdateProductCommand
        {
            Id = 1,
            Name = "Updated Product",
            Description = "Updated Description",
            Currency = "USD"
        };

        var validator = new UpdateProductCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Price));
        Assert.Contains(errors, error => error.PropertyName == nameof(command.Stock));
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("12!")]
    [InlineData("US1")]
    public void UpdateProductCommandValidator_WithInvalidCurrencyCode_ShouldReturnValidationError(string currency)
    {
        var command = new UpdateProductCommand
        {
            Id = 1,
            Name = "Updated Product",
            Description = "Updated Description",
            Price = 15.99m,
            Currency = currency,
            Stock = 50
        };

        var validator = new UpdateProductCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Currency));
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
        Assert.Equal(25.99m, command.Price!.Value);
        Assert.Equal("EUR", command.Currency);
        Assert.Equal(75, command.Stock!.Value);
    }
}
