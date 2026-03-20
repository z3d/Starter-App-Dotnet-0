namespace StarterApp.Api.Application.Validators;

public class UpdateProductCommandValidator : IValidator<UpdateProductCommand>
{
    public IEnumerable<ValidationError> Validate(UpdateProductCommand request)
    {
        if (request.Id <= 0)
            yield return new ValidationError(nameof(request.Id), "Id must be a positive integer");

        if (string.IsNullOrWhiteSpace(request.Name))
            yield return new ValidationError(nameof(request.Name), "Name is required");
        else if (request.Name.Length > Product.MaxNameLength)
            yield return new ValidationError(nameof(request.Name), $"Name must not exceed {Product.MaxNameLength} characters");

        if (request.Description == null)
            yield return new ValidationError(nameof(request.Description), "Description is required");
        else if (request.Description.Length > Product.MaxDescriptionLength)
            yield return new ValidationError(nameof(request.Description), $"Description must not exceed {Product.MaxDescriptionLength} characters");

        if (request.Price == null)
            yield return new ValidationError(nameof(request.Price), "Price is required");
        else if (request.Price < 0)
            yield return new ValidationError(nameof(request.Price), "Price cannot be negative");

        if (string.IsNullOrWhiteSpace(request.Currency))
            yield return new ValidationError(nameof(request.Currency), "Currency is required");
        else if (request.Currency.Length != 3)
            yield return new ValidationError(nameof(request.Currency), "Currency must be a 3-letter ISO code");

        if (request.Stock == null)
            yield return new ValidationError(nameof(request.Stock), "Stock is required");
        else if (request.Stock < 0)
            yield return new ValidationError(nameof(request.Stock), "Stock cannot be negative");
    }
}
