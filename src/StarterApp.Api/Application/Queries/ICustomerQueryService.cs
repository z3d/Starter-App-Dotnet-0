using StarterApp.Api.Application.ReadModels;

namespace StarterApp.Api.Application.Queries;

public interface ICustomerQueryService
{
    Task<IEnumerable<CustomerReadModel>> GetAllCustomersAsync();
    Task<CustomerReadModel?> GetCustomerByIdAsync(int id);
}