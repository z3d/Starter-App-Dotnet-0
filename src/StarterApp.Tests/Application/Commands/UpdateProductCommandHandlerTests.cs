namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class UpdateProductCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public UpdateProductCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldUpdateProductAndReturnDto()
    {
        // Arrange
        await using var context = CreateContext();

        // Create test product first
        var originalProduct = TestEntities.Product("Original Product", "Original Description", Money.Create(10.99m, "USD"), 100);
        context.Products.Add(originalProduct);
        await context.SaveChangesAsync();

        var handler = new UpdateProductCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);

        var command = new UpdateProductCommand
        {
            Id = originalProduct.Id,
            Name = "Updated Product",
            Description = "Updated Description",
            Price = 15.99m,
            Currency = "USD",
            Stock = 50
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.Id, result.Id);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Description, result.Description);
        Assert.Equal(command.Price!.Value, result.Price);
        Assert.Equal(command.Currency, result.Currency);
        Assert.Equal(command.Stock!.Value, result.Stock);

        // Verify the product was actually updated in the database
        var updatedProduct = await context.Products.FindAsync(command.Id);
        Assert.NotNull(updatedProduct);
        Assert.Equal(command.Name, updatedProduct.Name);
        Assert.Equal(command.Description, updatedProduct.Description);
        Assert.Equal(command.Price!.Value, updatedProduct.Price.Amount);
        Assert.Equal(command.Currency, updatedProduct.Price.Currency);
        Assert.Equal(command.Stock!.Value, updatedProduct.Stock);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ShouldThrowEntityNotFoundException()
    {
        // Arrange
        await using var context = CreateContext();
        var handler = new UpdateProductCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);

        var command = new UpdateProductCommand
        {
            Id = 99999, // Non-existent ID
            Name = "Updated Product",
            Description = "Updated Description",
            Price = 15.99m,
            Currency = "USD",
            Stock = 50
        };

        // Act & Assert
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => handler.HandleAsync(command, CancellationToken.None));
    }
}
