using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class GetOrdersByStatusQueryValidator : IValidator<GetOrdersByStatusQuery>
{
    public IEnumerable<ValidationError> Validate(GetOrdersByStatusQuery request)
    {
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
