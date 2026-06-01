using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class CreateCustomerCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public CreateCustomerCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

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

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateCustomerAndReturnDto()
    {
        // Arrange
        await using var context = CreateContext();
        var handler = new CreateCustomerCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);

        var command = new CreateCustomerCommand
        {
            Name = "John Doe",
            Email = "john@example.com"
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Email, result.Email);
        Assert.True(result.Id > 0);

        // Verify the customer was actually saved to the database
        var savedCustomer = await context.Customers.FirstOrDefaultAsync(c => c.Name == command.Name);
        Assert.NotNull(savedCustomer);
        Assert.Equal(command.Name, savedCustomer.Name);
        Assert.Equal(command.Email, savedCustomer.Email.Value);
    }
}
