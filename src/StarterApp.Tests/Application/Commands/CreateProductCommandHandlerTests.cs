namespace StarterApp.Tests.Application.Commands;

public class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductCommandService> _mockCommandService;
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _mockCommandService = new Mock<IProductCommandService>();
        _handler = new CreateProductCommandHandler(_mockCommandService.Object);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateProductAndReturnDto()
    {
        // Arrange
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = "USD",
            Stock = 100
        };

        // Setup mock to return a product with an ID assigned
        _mockCommandService.Setup(s => s.CreateProductAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Money>(),
                It.IsAny<int>()))
            .ReturnsAsync((string name, string description, Money price, int stock) => {
                var product = new Product(name, description, price, stock);
                product.SetId(1);
                return product;
            });

        // Act
        // Call the IRequestHandler<CreateProductCommand, ProductDto>.Handle method explicitly
        var result = await ((IRequestHandler<CreateProductCommand, ProductDto>)_handler)
            .Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Description, result.Description);
        Assert.Equal(command.Price, result.Price);
        Assert.Equal(command.Currency, result.Currency);
        Assert.Equal(command.Stock, result.Stock);

        _mockCommandService.Verify(s => s.CreateProductAsync(
            command.Name,
            command.Description,
            It.Is<Money>(m => m.Amount == command.Price && m.Currency == command.Currency),
            command.Stock), 
            Times.Once);
    }
}