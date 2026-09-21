using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class UpdateCustomerCommandTests
{
    [Fact]
    public void UpdateCustomerCommandValidator_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = 1,
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        var validator = new UpdateCustomerCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void UpdateCustomerCommandValidator_WithDisplayNameEmail_ShouldReturnValidationError()
    {
        var command = new UpdateCustomerCommand
        {
            Id = 1,
            Name = "Updated Name",
            Email = "Updated Name <updated@example.com>"
        };

        var validator = new UpdateCustomerCommandValidator();

        var errors = validator.Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(command.Email));
    }

    [Fact]
    public void UpdateCustomerCommand_PropertiesTest()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = 123,
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        // Act & Assert
        Assert.Equal(123, command.Id);
        Assert.Equal("Updated Name", command.Name);
        Assert.Equal("updated@example.com", command.Email);
    }
}
