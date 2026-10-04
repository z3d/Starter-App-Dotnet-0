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
    public void UpdateProductCommandValidator_WithNonPositiveId_ShouldReturnValidationError()
    {
        var command = new UpdateProductCommand { Id = 0, Name = "Updated Product", Description = "Updated Description", Price = 15.99m, Currency = "USD", Stock = 50 };

        var error = Assert.Single(new UpdateProductCommandValidator().Validate(command));

        Assert.Equal(nameof(command.Id), error.PropertyName);
    }

    [Fact]
    public void UpdateProductCommandValidator_WithAbsentDescription_ShouldReturnValidationError()
    {
        var command = new UpdateProductCommand { Id = 1, Name = "Updated Product", Description = null!, Price = 15.99m, Currency = "USD", Stock = 50 };

        var error = Assert.Single(new UpdateProductCommandValidator().Validate(command));

        Assert.Equal(nameof(command.Description), error.PropertyName);
    }
}
