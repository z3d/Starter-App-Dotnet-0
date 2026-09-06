namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class CreateProductCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public CreateProductCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateProductAndReturnDto()
    {
        // Arrange
        await using var context = CreateContext();
        var handler = new CreateProductCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);

        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = "USD",
            Stock = 100
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Description, result.Description);
        Assert.Equal(command.Price, result.Price);
        Assert.Equal(command.Currency, result.Currency);
        Assert.Equal(command.Stock, result.Stock);
        Assert.True(result.Id > 0);

        // Verify the product was actually saved to the database
        var savedProduct = await context.Products.FirstOrDefaultAsync(p => p.Name == command.Name);
        Assert.NotNull(savedProduct);
        Assert.Equal(command.Name, savedProduct.Name);
        Assert.Equal(command.Description, savedProduct.Description);
        Assert.Equal(command.Price, savedProduct.Price.Amount);
        Assert.Equal(command.Currency, savedProduct.Price.Currency);
        Assert.Equal(command.Stock, savedProduct.Stock);
    }
}
