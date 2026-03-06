using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class GetCustomerQueryValidator : IValidator<GetCustomerQuery>
{
    public IEnumerable<ValidationError> Validate(GetCustomerQuery request)
    {
        if (request.Id <= 0)
            yield return new ValidationError(nameof(request.Id), "Id must be a positive integer");
    }
}
