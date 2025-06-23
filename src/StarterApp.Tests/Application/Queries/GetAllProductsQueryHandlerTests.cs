using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Queries;
using StarterApp.Api.Application.ReadModels;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StarterApp.Tests.Application.Queries;

public class GetAllProductsQueryHandlerTests
{
    private readonly Mock<IProductQueryService> _mockQueryService;
    private readonly GetAllProductsQueryHandler _handler;

    public GetAllProductsQueryHandlerTests()
    {
        _mockQueryService = new Mock<IProductQueryService>();
        _handler = new GetAllProductsQueryHandler(_mockQueryService.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnAllProducts()
    {
        // Arrange
        var products = new List<ProductReadModel>
        {
            CreateTestProductReadModel(1, "Product 1", "Description 1", 10.99m, 100),
            CreateTestProductReadModel(2, "Product 2", "Description 2", 20.99m, 50),
            CreateTestProductReadModel(3, "Product 3", "Description 3", 30.99m, 75)
        };

        _mockQueryService.Setup(r => r.GetAllProductsAsync())
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
        
        _mockQueryService.Verify(r => r.GetAllProductsAsync(), Times.Once);
    }

    private static ProductReadModel CreateTestProductReadModel(int id, string name, string description, decimal price, int stock)
    {
        return new ProductReadModel
        {
            Id = id,
            Name = name,
            Description = description,
            PriceAmount = price, 
            PriceCurrency = "USD",
            Stock = stock,
            LastUpdated = DateTime.UtcNow
        };
    }
}