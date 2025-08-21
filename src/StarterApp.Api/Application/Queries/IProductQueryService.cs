using StarterApp.Api.Application.ReadModels;

namespace StarterApp.Api.Application.Queries;
public interface IProductQueryService
{
    Task<IEnumerable<ProductReadModel>> GetAllProductsAsync();
    Task<ProductReadModel?> GetProductByIdAsync(int id);
}
