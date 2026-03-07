using Microsoft.EntityFrameworkCore;
using StarterApp.Api.Data;

namespace StarterApp.Tests.Application.Commands;

public class DeleteCustomerCommandHandlerTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public void DeleteCustomerCommand_PropertiesTest()
    {
        // Arrange & Act
        var command = new DeleteCustomerCommand { Id = 42 };

        // Assert
        Assert.Equal(42, command.Id);
    }

    [Fact]
    public async Task Handle_WithExistingCustomer_ShouldDeleteCustomer()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var customer = new Customer("John Doe", Email.Create("john@example.com"));
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var handler = new DeleteCustomerCommandHandler(context);
        var command = new DeleteCustomerCommand { Id = customer.Id };

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        var deletedCustomer = await context.Customers.FindAsync(customer.Id);
        Assert.Null(deletedCustomer);
    }

    [Fact]
    public async Task Handle_WithNonExistentCustomer_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        await using var context = new ApplicationDbContext(options);

        var handler = new DeleteCustomerCommandHandler(context);
        var command = new DeleteCustomerCommand { Id = 999 };

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }
}
