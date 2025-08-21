using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class CreateCustomerCommandHandlerTests
{
    [Fact]
    public void CreateCustomerCommand_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var command = new CreateCustomerCommand
        {
            Name = "John Doe",
            Email = "john@example.com"
        };

        var validationContext = new ValidationContext(command);
        List<ValidationResult> validationResults = [];

        // Act
        var isValid = Validator.TryValidateObject(command, validationContext, validationResults, true);

        // Assert
        Assert.True(isValid);
        Assert.Empty(validationResults);
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
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var handler = new CreateCustomerCommandHandler(context);

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
