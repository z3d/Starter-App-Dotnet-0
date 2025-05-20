using DockerLearning.Domain.Entities;
using DockerLearning.Domain.ValueObjects;
using DockerLearningApi.Data;

namespace DockerLearningApi.Application.Commands;

/// <summary>
/// Service for product write operations using EF Core directly
/// </summary>
public class ProductCommandService : IProductCommandService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProductCommandService> _logger;

    public ProductCommandService(ApplicationDbContext dbContext, ILogger<ProductCommandService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Product> CreateProductAsync(string name, string description, Money price, int stock)
    {
        _logger.LogInformation("Creating product {Name} with EF Core", name);
        
        var product = new Product(name, description, price, stock);
        
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();
        
        return product;
    }

    public async Task<Product?> UpdateProductAsync(int id, string name, string description, Money price, int stock)
    {
        _logger.LogInformation("Updating product {Id} with EF Core", id);
        
        var product = await _dbContext.Products.FindAsync(id);
        if (product == null)
        {
            _logger.LogWarning("Product {Id} not found for update", id);
            return null;
        }

        product.UpdateDetails(name, description, price);
        
        // Handle stock changes
        if (product.Stock != stock)
        {
            var stockDifference = stock - product.Stock;
            product.UpdateStock(stockDifference);
        }
        
        await _dbContext.SaveChangesAsync();
        return product;
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        _logger.LogInformation("Deleting product {Id} with EF Core", id);
        
        var product = await _dbContext.Products.FindAsync(id);
        if (product == null)
        {
            _logger.LogWarning("Product {Id} not found for deletion", id);
            return false;
        }
        
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync();
        return true;
    }
}