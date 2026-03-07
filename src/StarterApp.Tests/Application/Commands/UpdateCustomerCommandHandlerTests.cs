using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class UpdateCustomerCommandHandlerTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public void UpdateCustomerCommand_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = 1,
            Name = "Updated Name",
            Email = "updated@example.com"
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

    [Fact]
    public async Task Handle_WithValidCommand_ShouldUpdateCustomerAndReturnDto()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("Original Name", Email.Create("original@example.com"));
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var handler = new UpdateCustomerCommandHandler(context);
        var command = new UpdateCustomerCommand
        {
            Id = customer.Id,
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.Id, result.Id);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Email, result.Email);
        Assert.True(result.IsActive);

        // Verify the customer was actually updated in the database
        var updatedCustomer = await context.Customers.FindAsync(command.Id);
        Assert.NotNull(updatedCustomer);
        Assert.Equal(command.Name, updatedCustomer.Name);
        Assert.Equal(command.Email, updatedCustomer.Email.Value);
    }

    [Fact]
    public async Task Handle_WithNonExistentCustomer_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var handler = new UpdateCustomerCommandHandler(context);
        var command = new UpdateCustomerCommand
        {
            Id = 99999,
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }
}
