using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

public class DeleteProductCommandTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DeleteProductCommandValidator_WithNonPositiveId_ShouldReturnValidationError(int id)
    {
        var errors = new DeleteProductCommandValidator().Validate(new DeleteProductCommand(id)).ToList();

        var error = Assert.Single(errors);
        Assert.Equal(nameof(DeleteProductCommand.Id), error.PropertyName);
    }

    [Fact]
    public void DeleteProductCommandValidator_WithPositiveId_ShouldPassValidation()
    {
        Assert.Empty(new DeleteProductCommandValidator().Validate(new DeleteProductCommand(1)));
    }
}
