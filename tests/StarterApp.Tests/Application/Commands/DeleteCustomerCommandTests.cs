using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class DeleteCustomerCommandTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DeleteCustomerCommandValidator_WithNonPositiveId_ShouldReturnValidationError(int id)
    {
        var errors = new DeleteCustomerCommandValidator().Validate(new DeleteCustomerCommand { Id = id }).ToList();

        var error = Assert.Single(errors);
        Assert.Equal(nameof(DeleteCustomerCommand.Id), error.PropertyName);
    }

    [Fact]
    public void DeleteCustomerCommandValidator_WithPositiveId_ShouldPassValidation()
    {
        Assert.Empty(new DeleteCustomerCommandValidator().Validate(new DeleteCustomerCommand { Id = 1 }));
    }
}
