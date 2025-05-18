using System.Net;
using System.Net.Http.Json;
using DockerLearningApi.Application.Commands;
using DockerLearningApi.Application.DTOs;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace DockerLearningApi.Tests.Integration;

[Collection("Integration Tests")]
public class ProductApiTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ProductApiTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Reset database before each test
        try
        {
            _output.WriteLine("Resetting database for test");
            await _fixture.ResetDatabaseAsync();
            _output.WriteLine("Database reset complete");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error during database reset: {ex.GetType().Name}");
            // Don't log the full exception details to avoid leaking sensitive information
            throw; // Re-throw to fail the test
        }
    }

    public async Task DisposeAsync()
    {
        // Explicit cleanup to ensure resources are released
        try
        {
            // No specific cleanup needed for each test, but good to have the structure
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: Cleanup error: {ex.Message}");
            // Log but don't throw to allow test to complete
        }
    }

    [Fact]
    public async Task CreateProduct_ShouldReturnCreatedProduct()
    {
        // Arrange
        var newProduct = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "A test product for integration testing",
            Price = 19.99m,
            Currency = "USD",
            Stock = 100
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/products", newProduct);

        // Assert
        response.EnsureSuccessStatusCode();
        var createdProduct = await response.Content.ReadFromJsonAsync<ProductDto>();
        
        Assert.NotNull(createdProduct);
        Assert.Equal(newProduct.Name, createdProduct.Name);
        Assert.Equal(newProduct.Description, createdProduct.Description);
        Assert.Equal(newProduct.Price, createdProduct.Price);
        Assert.Equal(newProduct.Currency, createdProduct.Currency);
        Assert.Equal(newProduct.Stock, createdProduct.Stock);
        Assert.True(createdProduct.Id > 0);
    }

    [Fact]
    public async Task GetProduct_WithValidId_ShouldReturnProduct()
    {
        // Arrange
        var newProduct = new CreateProductCommand
        {
            Name = "Product to Get",
            Description = "This product will be retrieved",
            Price = 29.99m,
            Currency = "USD",
            Stock = 50
        };

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/products", newProduct);
        createResponse.EnsureSuccessStatusCode();
        var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(createdProduct);

        // Act
        var response = await _fixture.Client.GetAsync($"/api/products/{createdProduct.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var retrievedProduct = await response.Content.ReadFromJsonAsync<ProductDto>();
        
        Assert.NotNull(retrievedProduct);
        Assert.Equal(createdProduct.Id, retrievedProduct.Id);
        Assert.Equal(newProduct.Name, retrievedProduct.Name);
    }

    [Fact]
    public async Task GetProducts_ShouldReturnAllProducts()
    {
        // Arrange
        var products = new[]
        {
            new CreateProductCommand
            {
                Name = "Product 1",
                Description = "First test product",
                Price = 10.99m,
                Currency = "USD",
                Stock = 10
            },
            new CreateProductCommand
            {
                Name = "Product 2",
                Description = "Second test product",
                Price = 20.99m,
                Currency = "USD",
                Stock = 20
            }
        };

        foreach (var product in products)
        {
            var response = await _fixture.Client.PostAsJsonAsync("/api/products", product);
            response.EnsureSuccessStatusCode();
        }

        // Act
        var getResponse = await _fixture.Client.GetAsync("/api/products");

        // Assert
        getResponse.EnsureSuccessStatusCode();
        var retrievedProducts = await getResponse.Content.ReadFromJsonAsync<IEnumerable<ProductDto>>();
        
        Assert.NotNull(retrievedProducts);
        Assert.Equal(products.Length, retrievedProducts.Count());
    }

    [Fact]
    public async Task UpdateProduct_WithValidId_ShouldUpdateProduct()
    {
        // Arrange
        var newProduct = new CreateProductCommand
        {
            Name = "Product to Update",
            Description = "This product will be updated",
            Price = 39.99m,
            Currency = "USD",
            Stock = 75
        };

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/products", newProduct);
        createResponse.EnsureSuccessStatusCode();
        var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(createdProduct);

        var updateCommand = new UpdateProductCommand
        {
            Id = createdProduct.Id,
            Name = "Updated Product Name",
            Description = "Updated description",
            Price = 49.99m,
            Currency = "USD"
        };

        // Act
        var updateResponse = await _fixture.Client.PutAsJsonAsync($"/api/products/{createdProduct.Id}", updateCommand);

        // Assert
        updateResponse.EnsureSuccessStatusCode();
        
        // Verify update with GET
        var getResponse = await _fixture.Client.GetAsync($"/api/products/{createdProduct.Id}");
        getResponse.EnsureSuccessStatusCode();
        var updatedProduct = await getResponse.Content.ReadFromJsonAsync<ProductDto>();
        
        Assert.NotNull(updatedProduct);
        Assert.Equal(updateCommand.Name, updatedProduct.Name);
        Assert.Equal(updateCommand.Description, updatedProduct.Description);
        Assert.Equal(updateCommand.Price, updatedProduct.Price);
    }

    [Fact]
    public async Task DeleteProduct_WithValidId_ShouldRemoveProduct()
    {
        // Arrange
        var newProduct = new CreateProductCommand
        {
            Name = "Product to Delete",
            Description = "This product will be deleted",
            Price = 15.99m,
            Currency = "USD",
            Stock = 30
        };

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/products", newProduct);
        createResponse.EnsureSuccessStatusCode();
        var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(createdProduct);

        // Act
        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/products/{createdProduct.Id}");

        // Assert
        deleteResponse.EnsureSuccessStatusCode();
        
        // Verify deletion
        var getResponse = await _fixture.Client.GetAsync($"/api/products/{createdProduct.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}