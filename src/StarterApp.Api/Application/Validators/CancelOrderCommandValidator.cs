using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class CancelOrderCommandValidator : IValidator<CancelOrderCommand>
{
    public IEnumerable<ValidationError> Validate(CancelOrderCommand request)
    {
        if (request.OrderId <= 0)
            yield return new ValidationError(nameof(request.OrderId), "OrderId must be a positive integer");
    }
}
