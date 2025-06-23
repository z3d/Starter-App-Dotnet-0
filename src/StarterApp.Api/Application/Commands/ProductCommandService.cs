using StarterApp.Api.Data;

namespace StarterApp.Api.Application.Commands;

/// <summary>
/// Service for product write operations using EF Core directly
/// </summary>
public class ProductCommandService : IProductCommandService
{
    private readonly ApplicationDbContext _dbContext;

    public ProductCommandService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Product> CreateProductAsync(string name, string description, Money price, int stock)
    {
        Log.Information("Creating product {Name} with EF Core", name);
        
        var product = new Product(name, description, price, stock);
        
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();
        
        return product;
    }

    public async Task<Product?> UpdateProductAsync(int id, string name, string description, Money price, int stock)
    {
        Log.Information("Updating product {Id} with EF Core", id);
        
        var product = await _dbContext.Products.FindAsync(id);
        if (product == null)
        {
            Log.Warning("Product {Id} not found for update", id);
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
        Log.Information("Deleting product {Id} with EF Core", id);
        
        var product = await _dbContext.Products.FindAsync(id);
        if (product == null)
        {
            Log.Warning("Product {Id} not found for deletion", id);
            return false;
        }
        
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync();
        return true;
    }
}