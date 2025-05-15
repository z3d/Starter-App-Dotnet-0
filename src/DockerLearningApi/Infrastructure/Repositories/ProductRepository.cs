using DockerLearningApi.Data;
using DockerLearningApi.Domain.Entities;
using DockerLearningApi.Domain.Interfaces;
using DockerLearningApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DockerLearningApi.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly ApplicationDbContext _context;

    public ProductRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        var productModel = await _context.Products.FindAsync(id);
        if (productModel == null)
            return null;

        // Map from data model to domain entity
        var product = Product.Create(
            productModel.Name,
            productModel.Description,
            Money.FromDecimal(productModel.Price),
            productModel.Stock);

        // Use internal method to set the ID
        ((dynamic)product).SetId(productModel.Id);
        
        return product;
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        var productModels = await _context.Products.ToListAsync();
        var products = new List<Product>();

        foreach (var productModel in productModels)
        {
            var product = Product.Create(
                productModel.Name,
                productModel.Description,
                Money.FromDecimal(productModel.Price),
                productModel.Stock);

            // Use internal method to set the ID
            ((dynamic)product).SetId(productModel.Id);
            
            products.Add(product);
        }

        return products;
    }

    public async Task<Product> AddAsync(Product product)
    {
        // Map from domain entity to data model
        var productModel = new Models.Product
        {
            Name = product.Name,
            Description = product.Description,
            Price = product.Price.Amount,
            Stock = product.Stock,
            LastUpdated = DateTime.UtcNow
        };

        _context.Products.Add(productModel);
        await _context.SaveChangesAsync();

        // Update domain entity with generated ID
        ((dynamic)product).SetId(productModel.Id);
        
        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        var productModel = await _context.Products.FindAsync(product.Id);
        if (productModel == null)
            throw new KeyNotFoundException($"Product with ID {product.Id} not found");

        // Update data model from domain entity
        productModel.Name = product.Name;
        productModel.Description = product.Description;
        productModel.Price = product.Price.Amount;
        productModel.Stock = product.Stock;
        productModel.LastUpdated = DateTime.UtcNow;

        _context.Products.Update(productModel);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var productModel = await _context.Products.FindAsync(id);
        if (productModel == null)
            throw new KeyNotFoundException($"Product with ID {id} not found");

        _context.Products.Remove(productModel);
        await _context.SaveChangesAsync();
    }
}