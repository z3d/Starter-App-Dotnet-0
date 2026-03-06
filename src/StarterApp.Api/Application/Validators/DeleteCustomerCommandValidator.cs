using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class DeleteCustomerCommandValidator : IValidator<DeleteCustomerCommand>
{
    public IEnumerable<ValidationError> Validate(DeleteCustomerCommand request)
    {
        if (request.Id <= 0)
            yield return new ValidationError(nameof(request.Id), "Id must be a positive integer");
    }
}
