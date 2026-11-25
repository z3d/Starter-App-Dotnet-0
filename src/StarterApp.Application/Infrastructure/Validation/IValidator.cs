namespace StarterApp.Application.Infrastructure.Validation;

public interface IValidator<in T>
{
    IEnumerable<ValidationError> Validate(T instance);
}
