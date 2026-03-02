namespace StarterApp.Api.Infrastructure.Validation;

public interface IValidator<in T>
{
    IEnumerable<ValidationError> Validate(T instance);
}

public record ValidationError(string PropertyName, string ErrorMessage);

public class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(IEnumerable<ValidationError> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors.ToList().AsReadOnly();
    }

    public override string Message =>
        string.Join("; ", Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
}
