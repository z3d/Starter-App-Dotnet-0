using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class CreateProductCommandValidator : IValidator<CreateProductCommand>
{
    public IEnumerable<ValidationError> Validate(CreateProductCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            yield return new ValidationError(nameof(request.Name), "Name is required");

        if (request.Price < 0)
            yield return new ValidationError(nameof(request.Price), "Price cannot be negative");

        if (string.IsNullOrWhiteSpace(request.Currency))
            yield return new ValidationError(nameof(request.Currency), "Currency is required");
        else if (request.Currency.Length != 3)
            yield return new ValidationError(nameof(request.Currency), "Currency must be a 3-letter ISO code");

        if (request.Stock < 0)
            yield return new ValidationError(nameof(request.Stock), "Stock cannot be negative");
    }
}
