using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class DeleteProductCommandValidator : IValidator<DeleteProductCommand>
{
    public IEnumerable<ValidationError> Validate(DeleteProductCommand request)
    {
        if (request.Id <= 0)
            yield return new ValidationError(nameof(request.Id), "Id must be a positive integer");
    }
}
