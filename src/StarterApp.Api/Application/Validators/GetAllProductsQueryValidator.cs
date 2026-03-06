using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class GetAllProductsQueryValidator : IValidator<GetAllProductsQuery>
{
    public IEnumerable<ValidationError> Validate(GetAllProductsQuery request)
    {
        if (request.Page <= 0)
            yield return new ValidationError(nameof(request.Page), "Page must be a positive integer");

        if (request.PageSize <= 0)
            yield return new ValidationError(nameof(request.PageSize), "PageSize must be a positive integer");
        else if (request.PageSize > 100)
            yield return new ValidationError(nameof(request.PageSize), "PageSize cannot exceed 100");
    }
}
