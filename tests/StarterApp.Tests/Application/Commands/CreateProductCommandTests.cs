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
    public void CreateProductCommandValidator_WithNegativePriceAndStock_ShouldReturnValidationErrors()
    {
        var command = new CreateProductCommand { Name = "Test Product", Description = "Test Description", Price = -0.01m, Currency = "USD", Stock = -1 };

        var errors = new CreateProductCommandValidator().Validate(command).ToList();

        Assert.Equal([nameof(command.Price), nameof(command.Stock)], errors.Select(error => error.PropertyName));
    }

    [Fact]
    public void CreateProductCommandValidator_HoldsDescriptionToTheDomainLimit()
    {
        var validator = new CreateProductCommandValidator();
        var atLimit = new CreateProductCommand { Name = "Test Product", Description = new string('d', Product.MaxDescriptionLength), Price = 1m, Currency = "USD", Stock = 1 };
        var overLimit = new CreateProductCommand { Name = "Test Product", Description = new string('d', Product.MaxDescriptionLength + 1), Price = 1m, Currency = "USD", Stock = 1 };

        Assert.Empty(validator.Validate(atLimit));
        var error = Assert.Single(validator.Validate(overLimit));
        Assert.Equal(nameof(overLimit.Description), error.PropertyName);
    }
}
