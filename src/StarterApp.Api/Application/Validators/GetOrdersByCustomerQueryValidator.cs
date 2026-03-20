namespace StarterApp.Api.Application.Validators;

public class GetOrdersByCustomerQueryValidator : IValidator<GetOrdersByCustomerQuery>
{
    public IEnumerable<ValidationError> Validate(GetOrdersByCustomerQuery request)
    {
        if (request.CustomerId <= 0)
            yield return new ValidationError(nameof(request.CustomerId), "CustomerId must be a positive integer");

        if (request.Page <= 0)
            yield return new ValidationError(nameof(request.Page), "Page must be a positive integer");

        if (request.PageSize <= 0)
            yield return new ValidationError(nameof(request.PageSize), "PageSize must be a positive integer");
        else if (request.PageSize > 100)
            yield return new ValidationError(nameof(request.PageSize), "PageSize cannot exceed 100");
    }
}
