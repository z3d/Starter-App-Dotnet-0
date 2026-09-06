using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class CreateOrderCommandValidatorTests
{
    [Theory]
    [InlineData("[null]", "Items[0]")]
    [InlineData("[{\"productId\":1,\"quantity\":1},null]", "Items[1]")]
    public void NullItem_ReturnsIndexedValidationError(string itemsJson, string propertyName)
    {
        var command = Deserialize(itemsJson);

        var errors = new CreateOrderCommandValidator().Validate(command).ToList();

        var error = Assert.Single(errors);
        Assert.Equal(propertyName, error.PropertyName);
        Assert.Equal("Order item must not be null", error.ErrorMessage);
    }

    [Fact]
    public void NullItems_WithInvalidAndDuplicateItems_ReturnsAllValidationErrors()
    {
        var command = Deserialize("""
            [null, {"productId":0,"quantity":0}, {"productId":2,"quantity":1},
             null, {"productId":2,"quantity":1}]
            """);

        var errors = new CreateOrderCommandValidator().Validate(command).ToList();

        Assert.Equal(["Items[0]", "Items[1].ProductId", "Items[1].Quantity", "Items[3]", "Items"],
            errors.Select(error => error.PropertyName));
        Assert.Contains("Duplicate product IDs: 2", errors[^1].ErrorMessage);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public void NullOrEmptyItems_ReturnsRequiredItemsError(string itemsJson)
    {
        var command = Deserialize(itemsJson);

        var errors = new CreateOrderCommandValidator().Validate(command).ToList();

        var error = Assert.Single(errors);
        Assert.Equal("Items", error.PropertyName);
        Assert.Equal("Order must contain at least one item", error.ErrorMessage);
    }

    private static CreateOrderCommand Deserialize(string itemsJson)
    {
        return JsonSerializer.Deserialize<CreateOrderCommand>(
            $$"""{"customerId":1,"items":{{itemsJson}}}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
