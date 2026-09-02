using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

// Pure validator checks for the boundaries the 2026-09-02 review found unmirrored between the
// application validators and the domain guards. No fixture: these never touch the database.
public class ValidatorBoundaryTests
{
    [Fact]
    public void CreateOrderCommandValidator_WithQuantityAboveMaxQuantity_ShouldReturnValidationError()
    {
        var command = new CreateOrderCommand
        {
            CustomerId = 1,
            Items = [new CreateOrderItemCommand { ProductId = 1, Quantity = OrderItem.MaxQuantity + 1 }]
        };

        var errors = new CreateOrderCommandValidator().Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == "Items[0].Quantity");
    }

    [Fact]
    public void CreateOrderCommandValidator_WithQuantityAtMaxQuantity_ShouldPass()
    {
        var command = new CreateOrderCommand
        {
            CustomerId = 1,
            Items = [new CreateOrderItemCommand { ProductId = 1, Quantity = OrderItem.MaxQuantity }]
        };

        Assert.Empty(new CreateOrderCommandValidator().Validate(command));
    }

    [Fact]
    public void CreateProductCommandValidator_WithAbsentCurrency_ShouldReturnValidationError()
    {
        // Matches UpdateProductCommandValidator: an omitted currency is rejected on both verbs
        // instead of silently defaulting to USD on create.
        var command = new CreateProductCommand { Name = "Widget", Price = 10m, Stock = 1 };

        var errors = new CreateProductCommandValidator().Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(CreateProductCommand.Currency));
    }
}
