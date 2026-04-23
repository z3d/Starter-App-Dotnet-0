namespace StarterApp.Api.Application.Validators;

public class CancelOrderCommandValidator : IValidator<CancelOrderCommand>
{
    public IEnumerable<ValidationError> Validate(CancelOrderCommand request)
    {
        if (request.OrderId == Guid.Empty)
            yield return new ValidationError(nameof(request.OrderId), "OrderId must be a non-empty Guid");
    }
}
