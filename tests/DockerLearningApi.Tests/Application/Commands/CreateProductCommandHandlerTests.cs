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
                typeof(Product).GetProperty("Id")
                    .SetValue(p, 1, null);
                return p;
            });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.Name.Should().Be(command.Name);
        result.Description.Should().Be(command.Description);
        result.Price.Should().Be(command.Price);
        result.Currency.Should().Be(command.Currency);
        result.Stock.Should().Be(command.Stock);

        _mockRepository.Verify(r => r.AddAsync(It.Is<Product>(p => 
            p.Name == command.Name && 
            p.Description == command.Description && 
            p.Price.Amount == command.Price && 
            p.Price.Currency == command.Currency && 
            p.Stock == command.Stock)), 
            Times.Once);
    }
}