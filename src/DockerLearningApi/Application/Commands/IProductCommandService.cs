namespace DockerLearningApi.Application.Commands;

/// <summary>
/// Interface for product command operations
/// </summary>
public interface IProductCommandService
{
    /// <summary>
    /// Creates a new product
    /// </summary>
    Task<Product> CreateProductAsync(string name, string description, Money price, int stock);
    
    /// <summary>
    /// Updates an existing product
    /// </summary>
    Task<Product?> UpdateProductAsync(int id, string name, string description, Money price, int stock);
    
    /// <summary>
    /// Deletes a product by ID
    /// </summary>
    Task<bool> DeleteProductAsync(int id);
}