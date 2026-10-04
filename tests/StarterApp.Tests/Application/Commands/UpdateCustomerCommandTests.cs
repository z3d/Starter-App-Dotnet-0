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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateCustomerCommandValidator_WithNonPositiveId_ShouldReturnValidationError(int id)
    {
        var command = new UpdateCustomerCommand { Id = id, Name = "Updated Name", Email = "updated@example.com" };

        var errors = new UpdateCustomerCommandValidator().Validate(command).ToList();

        var error = Assert.Single(errors);
        Assert.Equal(nameof(command.Id), error.PropertyName);
    }
}
