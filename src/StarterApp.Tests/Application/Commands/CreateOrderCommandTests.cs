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
}
