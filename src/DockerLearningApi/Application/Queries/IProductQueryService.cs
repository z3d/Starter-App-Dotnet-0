using DockerLearningApi.Application.ReadModels;

namespace DockerLearningApi.Application.Queries;

/// <summary>
/// Interface for product query operations
/// </summary>
public interface IProductQueryService
{
    /// <summary>
    /// Gets all products
    /// </summary>
    Task<IEnumerable<ProductReadModel>> GetAllProductsAsync();
    
    /// <summary>
    /// Gets a product by ID
    /// </summary>
    Task<ProductReadModel?> GetProductByIdAsync(int id);
}