using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application;

public class CommandValidatorBoundaryTests
{
    [Fact]
    public void CreateProductValidator_WithPriceAboveMaxAmount_ShouldFailValidation()
    {
        // Without an upper bound, 1e17 passes validation and overflows numeric(18,2)
        // (SqlState 22003) into a client-input-driven 500.
        var validator = new CreateProductCommandValidator();
        var command = new CreateProductCommand { Name = "Widget", Description = "d", Price = Money.MaxAmount + 1m, Currency = "USD", Stock = 1 };

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Price));
    }

    [Fact]
    public void CreateProductValidator_WithPriceAtMaxAmount_ShouldPassValidation()
    {
        var validator = new CreateProductCommandValidator();
        var command = new CreateProductCommand { Name = "Widget", Description = "d", Price = Money.MaxAmount, Currency = "USD", Stock = 1 };

        Assert.Empty(validator.Validate(command));
    }

    [Fact]
    public void CreateProductValidator_WithAbsentPrice_ShouldFailValidation()
    {
        // A non-nullable Price deserialized an absent "price" field to 0m, so a request that
        // forgot the field silently created a free product. Null must be rejected as missing.
        var validator = new CreateProductCommandValidator();
        var command = new CreateProductCommand { Name = "Widget", Description = "d", Price = null, Currency = "USD", Stock = 1 };

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Price) && error.ErrorMessage == "Price is required");
    }

    [Fact]
    public void CreateProductValidator_WithAbsentStock_ShouldFailValidation()
    {
        var validator = new CreateProductCommandValidator();
        var command = new CreateProductCommand { Name = "Widget", Description = "d", Price = 1m, Currency = "USD", Stock = null };

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Stock) && error.ErrorMessage == "Stock is required");
    }

    [Fact]
    public void CreateProductValidator_WithExplicitZeroPriceAndStock_ShouldPassValidation()
    {
        // Zero stays a legitimate explicit value (a free, unstocked product) — only absence fails.
        var validator = new CreateProductCommandValidator();
        var command = new CreateProductCommand { Name = "Widget", Description = "d", Price = 0m, Currency = "USD", Stock = 0 };

        Assert.Empty(validator.Validate(command));
    }

    [Fact]
    public void UpdateProductValidator_WithPriceAboveMaxAmount_ShouldFailValidation()
    {
        var validator = new UpdateProductCommandValidator();
        var command = new UpdateProductCommand { Id = 1, Name = "Widget", Description = "d", Price = Money.MaxAmount + 1m, Currency = "USD", Stock = 1 };

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Price));
    }

    [Fact]
    public void UpdateOrderStatusValidator_WithUndefinedEnumValue_ShouldFailValidation()
    {
        // {"status": 99} binds as an undefined enum; without this rule it misroutes to the domain
        // guard and leaks a 409 "Cannot transition from Pending to 99" instead of returning 400.
        var validator = new UpdateOrderStatusCommandValidator();
        var command = new UpdateOrderStatusCommand { OrderId = Guid.CreateVersion7(), Status = (OrderStatus)99 };

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Status));
    }

    [Fact]
    public void UpdateOrderStatusValidator_WithDefinedEnumValue_ShouldPassValidation()
    {
        var validator = new UpdateOrderStatusCommandValidator();
        var command = new UpdateOrderStatusCommand { OrderId = Guid.CreateVersion7(), Status = OrderStatus.Confirmed };

        Assert.Empty(validator.Validate(command));
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

    [Theory]
    [InlineData(10.999, "USD")]
    [InlineData(10.5, "JPY")]
    public void ProductValidators_WithAPricePastItsCurrencysScale_RefuseThePrice(decimal price, string currency)
    {
        var create = new CreateProductCommand { Name = "Widget", Description = "d", Price = price, Currency = currency, Stock = 1 };
        var update = new UpdateProductCommand { Id = 1, Name = "Widget", Description = "d", Price = price, Currency = currency, Stock = 1 };

        Assert.Contains(new CreateProductCommandValidator().Validate(create), error => error.PropertyName == nameof(create.Price));
        Assert.Contains(new UpdateProductCommandValidator().Validate(update), error => error.PropertyName == nameof(update.Price));
    }

    [Fact]
    public void ProductValidators_WithACurrencyOfMorePlacesThanAPriceStores_RefuseTheCurrency()
    {
        var create = new CreateProductCommand { Name = "Widget", Description = "d", Price = 1.234m, Currency = "KWD", Stock = 1 };
        var update = new UpdateProductCommand { Id = 1, Name = "Widget", Description = "d", Price = 1.234m, Currency = "KWD", Stock = 1 };

        Assert.Contains(new CreateProductCommandValidator().Validate(create), error => error.PropertyName == nameof(create.Currency));
        Assert.Contains(new UpdateProductCommandValidator().Validate(update), error => error.PropertyName == nameof(update.Currency));
    }
}
