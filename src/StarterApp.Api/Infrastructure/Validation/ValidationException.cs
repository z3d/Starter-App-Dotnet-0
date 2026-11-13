namespace StarterApp.Api.Infrastructure.Validation;

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
