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
        }

        var duplicateProductIds = request.Items
            .GroupBy(item => item.ProductId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateProductIds.Count > 0)
        {
            yield return new ValidationError(
                nameof(request.Items),
                $"Each product may only appear once per order. Duplicate product IDs: {string.Join(", ", duplicateProductIds)}");
        }
    }
}
