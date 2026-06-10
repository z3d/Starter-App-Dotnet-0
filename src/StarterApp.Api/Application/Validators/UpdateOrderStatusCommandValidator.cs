namespace StarterApp.Api.Application.Validators;

public class UpdateOrderStatusCommandValidator : IValidator<UpdateOrderStatusCommand>
{
    public IEnumerable<ValidationError> Validate(UpdateOrderStatusCommand request)
    {
        if (request.OrderId == Guid.Empty)
            yield return new ValidationError(nameof(request.OrderId), "OrderId must be a non-empty Guid");

        if (!request.Status.HasValue)
            yield return new ValidationError(nameof(request.Status), "Status is required");
        else if (!Enum.IsDefined(request.Status.Value))
            yield return new ValidationError(nameof(request.Status), "Status is not a valid order status");
    }
}
