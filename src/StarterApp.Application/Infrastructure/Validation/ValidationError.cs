namespace StarterApp.Application.Infrastructure.Validation;

public record ValidationError(string PropertyName, string ErrorMessage);
