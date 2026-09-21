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

    [Fact]
    public void CreateCustomerCommand_PropertiesTest()
    {
        // Arrange
        var command = new CreateCustomerCommand
        {
            Name = "John Doe",
            Email = "john@example.com"
        };

        // Act & Assert - Verify all properties are set correctly
        Assert.Equal("John Doe", command.Name);
        Assert.Equal("john@example.com", command.Email);
    }
}
