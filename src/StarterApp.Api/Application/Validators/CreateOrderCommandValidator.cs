using StarterApp.Api.Infrastructure.Idempotency;

namespace StarterApp.Api.Application.Validators;

public class CreateOrderCommandValidator : IValidator<CreateOrderCommand>
{
    private const int MaxItemsPerOrder = Order.MaxItems;
    public IEnumerable<ValidationError> Validate(CreateOrderCommand request)
    {
        if (request.CustomerId <= 0)
            yield return new ValidationError(nameof(request.CustomerId), "CustomerId must be a positive integer");

        if (request.IdempotencyKey is { } key && (key.Length is 0 or > IdempotencyRecord.MaxKeyLength || key.Any(c => c is < '!' or > '~')))
            yield return new ValidationError("Idempotency-Key", $"Idempotency-Key must be 1 to {IdempotencyRecord.MaxKeyLength} visible ASCII characters");

        if (request.Items == null || request.Items.Count == 0)
        {
            yield return new ValidationError(nameof(request.Items), "Order must contain at least one item");
            yield break;
        }

        if (request.Items.Count > MaxItemsPerOrder)
        {
            yield return new ValidationError(nameof(request.Items), $"Order cannot contain more than {MaxItemsPerOrder} items");
            yield break;
        }

        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (item is null)
            {
                yield return new ValidationError($"Items[{i}]", "Order item must not be null");
                continue;
            }

            if (item.ProductId <= 0)
                yield return new ValidationError($"Items[{i}].ProductId", "ProductId must be a positive integer");

            if (item.Quantity <= 0)
                yield return new ValidationError($"Items[{i}].Quantity", "Quantity must be a positive integer");
        }

        var duplicateProductIds = request.Items
            .Where(item => item is not null)
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
