namespace StarterApp.Api.Application.Validators;

public class CreateProductCommandValidator : IValidator<CreateProductCommand>
{
    public IEnumerable<ValidationError> Validate(CreateProductCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            yield return new ValidationError(nameof(request.Name), "Name is required");
        else if (request.Name.Length > Product.MaxNameLength)
            yield return new ValidationError(nameof(request.Name), $"Name must not exceed {Product.MaxNameLength} characters");

        if (request.Description?.Length > Product.MaxDescriptionLength)
            yield return new ValidationError(nameof(request.Description), $"Description must not exceed {Product.MaxDescriptionLength} characters");

        if (request.Price is null)
            yield return new ValidationError(nameof(request.Price), "Price is required");
        else if (request.Price < 0)
            yield return new ValidationError(nameof(request.Price), "Price cannot be negative");
        else if (request.Price > Money.MaxAmount)
            yield return new ValidationError(nameof(request.Price), $"Price must not exceed {Money.MaxAmount}");
        else if (Money.IsValidCurrencyCode(request.Currency) && !Money.IsAtScale(request.Price.Value, request.Currency!))
            yield return new ValidationError(nameof(request.Price), "Price has more decimal places than its currency allows; it is never rounded");

        if (string.IsNullOrWhiteSpace(request.Currency))
            yield return new ValidationError(nameof(request.Currency), "Currency is required");
        else if (!Money.IsValidCurrencyCode(request.Currency))
            yield return new ValidationError(nameof(request.Currency), "Currency must be a 3-letter ISO code");
        else if (Money.MinorUnits(request.Currency) > Product.PriceDecimalPlaces)
            yield return new ValidationError(nameof(request.Currency), $"Currency has more decimal places than a product price stores ({Product.PriceDecimalPlaces})");

        if (request.Stock is null)
            yield return new ValidationError(nameof(request.Stock), "Stock is required");
        else if (request.Stock < 0)
            yield return new ValidationError(nameof(request.Stock), "Stock cannot be negative");
    }
}
