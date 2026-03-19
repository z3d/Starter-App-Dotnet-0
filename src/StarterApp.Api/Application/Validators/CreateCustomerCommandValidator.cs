using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Application.Validators;

public class CreateCustomerCommandValidator : IValidator<CreateCustomerCommand>
{
    public IEnumerable<ValidationError> Validate(CreateCustomerCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            yield return new ValidationError(nameof(request.Name), "Name is required");

        if (string.IsNullOrWhiteSpace(request.Email))
            yield return new ValidationError(nameof(request.Email), "Email is required");
        else if (request.Email.Length > Email.MaxEmailLength)
            yield return new ValidationError(nameof(request.Email), $"Email must not exceed {Email.MaxEmailLength} characters");
        else if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
            yield return new ValidationError(nameof(request.Email), "Email must be a valid email address");
    }
}
