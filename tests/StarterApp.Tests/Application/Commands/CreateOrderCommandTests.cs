using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class CreateOrderCommandTests
{
    [Fact]
    public void CreateOrderCommandValidator_WithValidData_ShouldPassValidation()
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

        var validator = new CreateOrderCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void CreateOrderCommandValidator_WithMoreThanMaxItems_ShouldFailValidation()
    {
        var command = new CreateOrderCommand
        {
            CustomerId = 1,
            Items = Enumerable.Range(1, 51)
                .Select(productId => new CreateOrderItemCommand
                {
                    ProductId = productId,
                    Quantity = 1
                })
                .ToList()
        };

        var validator = new CreateOrderCommandValidator();

        var errors = validator.Validate(command).ToList();

        var error = Assert.Single(errors);
        Assert.Equal(nameof(command.Items), error.PropertyName);
        Assert.Contains("more than 50 items", error.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateOrderCommandValidator_WithNonPositiveCustomerId_ShouldReturnValidationError(int customerId)
    {
        var command = new CreateOrderCommand { CustomerId = customerId, Items = [new() { ProductId = 1, Quantity = 1 }] };

        var error = Assert.Single(new CreateOrderCommandValidator().Validate(command));

        Assert.Equal(nameof(command.CustomerId), error.PropertyName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("caf\u00e9")]
    [InlineData(256)]
    public void CreateOrderCommandValidator_WithUnusableIdempotencyKey_ShouldReturnValidationError(object key)
    {
        var command = new CreateOrderCommand
        {
            CustomerId = 1,
            IdempotencyKey = key is int length ? new string('k', length) : (string)key,
            Items = [new() { ProductId = 1, Quantity = 1 }]
        };

        var error = Assert.Single(new CreateOrderCommandValidator().Validate(command));

        Assert.Equal("Idempotency-Key", error.PropertyName);
    }

    [Fact]
    public void CreateOrderCommandValidator_WithLongestVisibleAsciiIdempotencyKey_ShouldPassValidation()
    {
        var command = new CreateOrderCommand { CustomerId = 1, IdempotencyKey = new string('~', 255), Items = [new() { ProductId = 1, Quantity = 1 }] };

        Assert.Empty(new CreateOrderCommandValidator().Validate(command));
    }
}
