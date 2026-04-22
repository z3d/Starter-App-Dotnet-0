namespace StarterApp.Api.Application.Validators;

public class UpdateOrderStatusCommandValidator : IValidator<UpdateOrderStatusCommand>
{
    public IEnumerable<ValidationError> Validate(UpdateOrderStatusCommand request)
    {
        if (request.OrderId == Guid.Empty)
            yield return new ValidationError(nameof(request.OrderId), "OrderId must be a non-empty Guid");

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            yield return new ValidationError(nameof(request.Status), "Status is required");
            yield break;
        }

        if (!Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out _))
        {
            var validStatuses = string.Join(", ", Enum.GetNames<OrderStatus>());
            yield return new ValidationError(nameof(request.Status), $"Invalid status '{request.Status}'. Valid values: {validStatuses}");
        }
    }
}
