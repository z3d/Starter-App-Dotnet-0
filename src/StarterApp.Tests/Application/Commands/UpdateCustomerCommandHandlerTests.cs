namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class UpdateCustomerCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public UpdateCustomerCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldUpdateCustomerAndReturnDto()
    {
        // Arrange
        await using var context = CreateContext();

        var customer = TestEntities.Customer("Original Name", Email.Create("original@example.com"));
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var handler = new UpdateCustomerCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
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
    public async Task Handle_WithNonExistentCustomer_ShouldThrowEntityNotFoundException()
    {
        // Arrange
        await using var context = CreateContext();

        var handler = new UpdateCustomerCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new UpdateCustomerCommand
        {
            Id = 99999,
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        // Act & Assert
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }
}
