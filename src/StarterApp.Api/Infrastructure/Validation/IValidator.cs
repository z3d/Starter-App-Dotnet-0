namespace StarterApp.Api.Infrastructure.Validation;

public interface IValidator<in T>
{
    IEnumerable<ValidationError> Validate(T instance);
}
