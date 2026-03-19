using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class CreateOrderCommandValidator : IValidator<CreateOrderCommand>
{
    public IEnumerable<ValidationError> Validate(CreateOrderCommand request)
    {
        if (request.CustomerId <= 0)
            yield return new ValidationError(nameof(request.CustomerId), "CustomerId must be a positive integer");

        if (request.Items == null || request.Items.Count == 0)
        {
            yield return new ValidationError(nameof(request.Items), "Order must contain at least one item");
            yield break;
        }

        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];

            if (item.ProductId <= 0)
                yield return new ValidationError($"Items[{i}].ProductId", "ProductId must be a positive integer");

            if (item.Quantity <= 0)
                yield return new ValidationError($"Items[{i}].Quantity", "Quantity must be a positive integer");

            if (item.UnitPriceExcludingGst < 0)
                yield return new ValidationError($"Items[{i}].UnitPriceExcludingGst", "UnitPriceExcludingGst cannot be negative");

            if (string.IsNullOrWhiteSpace(item.Currency))
                yield return new ValidationError($"Items[{i}].Currency", "Currency is required");
            else if (item.Currency.Length != 3)
                yield return new ValidationError($"Items[{i}].Currency", "Currency must be a 3-letter ISO code");

            if (item.GstRate < 0 || item.GstRate > 1.0m)
                yield return new ValidationError($"Items[{i}].GstRate", "GST rate must be between 0 and 1 (e.g., 0.10 for 10%)");
        }
    }
}
