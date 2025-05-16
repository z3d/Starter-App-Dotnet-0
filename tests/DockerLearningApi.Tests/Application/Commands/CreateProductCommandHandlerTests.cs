namespace DockerLearningApi.Tests.Application.Commands;

public class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductRepository> _mockRepository;
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _mockRepository = new Mock<IProductRepository>();
        _handler = new CreateProductCommandHandler(_mockRepository.Object);
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

        // Setup mock to return the product with an ID assigned
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<Product>()))
            .ReturnsAsync((Product p) => {
                var idProperty = typeof(Product).GetProperty("Id");
                if (idProperty != null)
                {
                    idProperty.SetValue(p, 1, null);
                }
                return p;
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

        _mockRepository.Verify(r => r.AddAsync(It.Is<Product>(p => 
            p.Name == command.Name && 
            p.Description == command.Description && 
            p.Price.Amount == command.Price && 
            p.Price.Currency == command.Currency && 
            p.Stock == command.Stock)), 
            Times.Once);
    }
}