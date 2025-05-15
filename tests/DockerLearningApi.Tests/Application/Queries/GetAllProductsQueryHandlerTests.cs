namespace DockerLearningApi.Tests.Application.Queries;

public class GetAllProductsQueryHandlerTests
{
    private readonly Mock<IProductRepository> _mockRepository;
    private readonly GetAllProductsQueryHandler _handler;

    public GetAllProductsQueryHandlerTests()
    {
        _mockRepository = new Mock<IProductRepository>();
        _handler = new GetAllProductsQueryHandler(_mockRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnAllProducts()
    {
        // Arrange
        var products = new List<Product>
        {
            CreateTestProduct(1, "Product 1", "Description 1", 10.99m, 100),
            CreateTestProduct(2, "Product 2", "Description 2", 20.99m, 50),
            CreateTestProduct(3, "Product 3", "Description 3", 30.99m, 75)
        };

        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(products);

        var query = new GetAllProductsQuery();

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        
        var resultList = result.ToList();
        
        resultList[0].Id.Should().Be(1);
        resultList[0].Name.Should().Be("Product 1");
        resultList[0].Price.Should().Be(10.99m);
        
        resultList[1].Id.Should().Be(2);
        resultList[1].Name.Should().Be("Product 2");
        resultList[1].Price.Should().Be(20.99m);
        
        resultList[2].Id.Should().Be(3);
        resultList[2].Name.Should().Be("Product 3");
        resultList[2].Price.Should().Be(30.99m);
        
        _mockRepository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    private static Product CreateTestProduct(int id, string name, string description, decimal price, int stock)
    {
        var product = Product.Create(name, description, Money.Create(price), stock);
        
        // Set the ID using reflection since it has a private setter
        typeof(Product).GetProperty("Id")
            .SetValue(product, id, null);
            
        return product;
    }
}