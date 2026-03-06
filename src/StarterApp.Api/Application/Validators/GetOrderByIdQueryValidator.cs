using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class GetOrderByIdQueryValidator : IValidator<GetOrderByIdQuery>
{
    public IEnumerable<ValidationError> Validate(GetOrderByIdQuery request)
    {
        if (request.Id <= 0)
            yield return new ValidationError(nameof(request.Id), "Id must be a positive integer");
    }
}
