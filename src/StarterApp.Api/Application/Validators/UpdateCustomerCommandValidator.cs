using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class UpdateCustomerCommandValidator : IValidator<UpdateCustomerCommand>
{
    public IEnumerable<ValidationError> Validate(UpdateCustomerCommand request)
    {
        if (request.Id <= 0)
            yield return new ValidationError(nameof(request.Id), "Id must be a positive integer");

        if (string.IsNullOrWhiteSpace(request.Name))
            yield return new ValidationError(nameof(request.Name), "Name is required");

        if (string.IsNullOrWhiteSpace(request.Email))
            yield return new ValidationError(nameof(request.Email), "Email is required");
        else if (!request.Email.Contains('@'))
            yield return new ValidationError(nameof(request.Email), "Email must be a valid email address");
    }
}
