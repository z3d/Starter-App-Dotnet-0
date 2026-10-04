using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class CreateCustomerCommandTests
{
    [Fact]
    public void CreateCustomerCommandValidator_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var command = new CreateCustomerCommand
        {
            Name = "John Doe",
            Email = "john@example.com"
        };

        var validator = new CreateCustomerCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void CreateCustomerCommandValidator_WithDisplayNameEmail_ShouldReturnValidationError()
    {
        var command = new CreateCustomerCommand
        {
            Name = "John Doe",
            Email = "John Doe <john@example.com>"
        };

        var validator = new CreateCustomerCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateCustomerCommandValidator_WithBlankName_ShouldReturnValidationError(string name)
    {
        var command = new CreateCustomerCommand { Name = name, Email = "john@example.com" };

        var errors = new CreateCustomerCommandValidator().Validate(command).ToList();

        var error = Assert.Single(errors);
        Assert.Equal(nameof(command.Name), error.PropertyName);
    }

    [Fact]
    public void CreateCustomerCommandValidator_HoldsNameToTheDomainLimit()
    {
        var validator = new CreateCustomerCommandValidator();
        var atLimit = new CreateCustomerCommand { Name = new string('a', Customer.MaxNameLength), Email = "john@example.com" };
        var overLimit = new CreateCustomerCommand { Name = new string('a', Customer.MaxNameLength + 1), Email = "john@example.com" };

        Assert.Empty(validator.Validate(atLimit));
        var error = Assert.Single(validator.Validate(overLimit));
        Assert.Equal(nameof(overLimit.Name), error.PropertyName);
    }
}
