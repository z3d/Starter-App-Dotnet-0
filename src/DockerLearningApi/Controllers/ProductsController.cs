using DockerLearningApi.Data;
using DockerLearningApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockerLearningApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(ApplicationDbContext context, ILogger<ProductsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // GET: api/Products
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
    {
        _logger.LogInformation("Getting all products");
        return await _context.Products.ToListAsync();
    }

    // GET: api/Products/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> GetProduct(int id)
    {
        _logger.LogInformation("Getting product with ID: {Id}", id);
        var product = await _context.Products.FindAsync(id);

        if (product == null)
        {
            _logger.LogWarning("Product with ID: {Id} not found", id);
            return NotFound();
        }

        return product;
    }

    // POST: api/Products
    [HttpPost]
    public async Task<ActionResult<Product>> CreateProduct(Product product)
    {
        // Set LastUpdated to current UTC time
        product.LastUpdated = DateTime.UtcNow;
        
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created new product with ID: {Id}", product.Id);
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }

    // PUT: api/Products/5
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(int id, Product product)
    {
        if (id != product.Id)
        {
            return BadRequest();
        }

        // Set LastUpdated to current UTC time
        product.LastUpdated = DateTime.UtcNow;
        
        _context.Entry(product).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Updated product with ID: {Id}", id);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await ProductExists(id))
            {
                _logger.LogWarning("Product with ID: {Id} not found during update", id);
                return NotFound();
            }
            throw;
        }

        return NoContent();
    }

    // DELETE: api/Products/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            _logger.LogWarning("Product with ID: {Id} not found during delete", id);
            return NotFound();
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted product with ID: {Id}", id);
        return NoContent();
    }

    private async Task<bool> ProductExists(int id)
    {
        return await _context.Products.AnyAsync(e => e.Id == id);
    }
}