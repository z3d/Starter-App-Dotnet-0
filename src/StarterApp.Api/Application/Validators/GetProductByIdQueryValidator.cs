using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class GetProductByIdQueryValidator : IValidator<GetProductByIdQuery>
{
    public IEnumerable<ValidationError> Validate(GetProductByIdQuery request)
    {
        if (request.Id <= 0)
            yield return new ValidationError(nameof(request.Id), "Id must be a positive integer");
    }
}
