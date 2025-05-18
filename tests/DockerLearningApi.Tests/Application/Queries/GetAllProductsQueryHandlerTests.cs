using DockerLearning.Domain.Entities;
using DockerLearning.Domain.Interfaces;
using DockerLearning.Domain.ValueObjects;
using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Queries;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

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
        Assert.NotNull(result);
        var resultList = result.ToList();
        Assert.Equal(3, resultList.Count);
        
        Assert.Equal(1, resultList[0].Id);
        Assert.Equal("Product 1", resultList[0].Name);
        Assert.Equal(10.99m, resultList[0].Price);
        
        Assert.Equal(2, resultList[1].Id);
        Assert.Equal("Product 2", resultList[1].Name);
        Assert.Equal(20.99m, resultList[1].Price);
        
        Assert.Equal(3, resultList[2].Id);
        Assert.Equal("Product 3", resultList[2].Name);
        Assert.Equal(30.99m, resultList[2].Price);
        
        _mockRepository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    private static Product CreateTestProduct(int id, string name, string description, decimal price, int stock)
    {
        var product = new Product(name, description, Money.Create(price), stock);
        
        // Use the public method to set the ID
        product.SetId(id);
            
        return product;
    }
}