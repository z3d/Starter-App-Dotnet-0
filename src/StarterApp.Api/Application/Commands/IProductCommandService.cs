namespace StarterApp.Api.Application.Commands;
public interface IProductCommandService
{
    Task<Product> CreateProductAsync(string name, string description, Money price, int stock);
    Task<Product?> UpdateProductAsync(int id, string name, string description, Money price, int stock);
    Task<bool> DeleteProductAsync(int id);
}



