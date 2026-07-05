
namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class DeleteCustomerCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public DeleteCustomerCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

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
        await using var context = CreateContext();

        var customer = TestEntities.Customer("John Doe", Email.Create("john@example.com"));
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var handler = new DeleteCustomerCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new DeleteCustomerCommand { Id = customer.Id };

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        var deletedCustomer = await context.Customers.FindAsync(customer.Id);
        Assert.Null(deletedCustomer);
    }

    [Fact]
    public async Task Handle_WithNonExistentCustomer_ShouldThrowEntityNotFoundException()
    {
        // Arrange
        await using var context = CreateContext();

        var handler = new DeleteCustomerCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new DeleteCustomerCommand { Id = 999 };

        // Act & Assert
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithCustomerWhoHasOrders_ShouldThrowDomainRuleException()
    {
        // Arrange
        await using var context = CreateContext();

        var customer = TestEntities.Customer("Test Customer", Email.Create("test@example.com"));
        var product = TestEntities.Product("Test Product", "Description", Money.Create(10.00m, "USD"), 100);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Create an order for this customer
        var createHandler = new CreateOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        await createHandler.HandleAsync(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }]
        }, CancellationToken.None);

        var handler = new DeleteCustomerCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new DeleteCustomerCommand { Id = customer.Id };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            handler.HandleAsync(command, CancellationToken.None));
        Assert.Contains("existing orders", ex.Message);
    }
}
