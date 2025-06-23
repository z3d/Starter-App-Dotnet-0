using StarterApp.Api.Data;

namespace StarterApp.Api.Infrastructure.Repositories;

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
        var product = new Product(
            productModel.Name,
            productModel.Description,
            Money.Create(productModel.Price.Amount, productModel.Price.Currency),
            productModel.Stock);

        // Use internal method to set the ID
        product.SetId(productModel.Id);
        
        return product;
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        var productModels = await _context.Products.ToListAsync();
        var products = new List<Product>();

        foreach (var productModel in productModels)
        {
            var product = new Product(
                productModel.Name,
                productModel.Description,
                Money.Create(productModel.Price.Amount, productModel.Price.Currency),
                productModel.Stock);

            // Use internal method to set the ID
            product.SetId(productModel.Id);
            
            products.Add(product);
        }

        return products;
    }

    public async Task<Product> AddAsync(Product product)
    {
        // Create a new domain entity using the constructor
        var productModel = new Product(
            product.Name,
            product.Description,
            Money.Create(product.Price.Amount, product.Price.Currency),
            product.Stock
        );

        _context.Products.Add(productModel);
        await _context.SaveChangesAsync();

        // Update domain entity with generated ID
        product.SetId(productModel.Id);
        
        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        var productModel = await _context.Products.FindAsync(product.Id);
        if (productModel == null)
            throw new KeyNotFoundException($"Product with ID {product.Id} not found");

        // Update data model from domain entity using domain methods
        productModel.UpdateDetails(
            product.Name,
            product.Description,
            Money.Create(product.Price.Amount, product.Price.Currency)
        );
        
        // Handle stock changes
        if (productModel.Stock != product.Stock)
        {
            var stockDifference = product.Stock - productModel.Stock;
            productModel.UpdateStock(stockDifference);
        }

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