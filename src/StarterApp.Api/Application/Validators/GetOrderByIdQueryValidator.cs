namespace StarterApp.Api.Application.Validators;

public class GetOrderByIdQueryValidator : IValidator<GetOrderByIdQuery>
{
    public IEnumerable<ValidationError> Validate(GetOrderByIdQuery request)
    {
        if (request.Id == Guid.Empty)
            yield return new ValidationError(nameof(request.Id), "Id must be a non-empty Guid");
    }
}
