namespace StarterApp.Api.Infrastructure.Validation;

public record ValidationError(string PropertyName, string ErrorMessage);
