namespace StarterApp.Api.Application.Validators;

public class GetOrdersByStatusQueryValidator : IValidator<GetOrdersByStatusQuery>
{
    public IEnumerable<ValidationError> Validate(GetOrdersByStatusQuery request)
    {
        if (!request.Status.HasValue)
            yield return new ValidationError(nameof(request.Status), "Status is required");

        if (request.Page <= 0)
            yield return new ValidationError(nameof(request.Page), "Page must be a positive integer");
        else if (request.Page > 100_000)
            yield return new ValidationError(nameof(request.Page), "Page cannot exceed 100000");

        if (request.PageSize <= 0)
            yield return new ValidationError(nameof(request.PageSize), "PageSize must be a positive integer");
        else if (request.PageSize > 100)
            yield return new ValidationError(nameof(request.PageSize), "PageSize cannot exceed 100");
    }
}
