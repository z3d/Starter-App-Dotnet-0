using StarterApp.Api.Application.ReadModels;

namespace StarterApp.Api.Application.Queries;

public interface IOrderQueryService
{
    Task<OrderWithItemsReadModel?> GetOrderByIdAsync(int id);
    Task<IEnumerable<OrderReadModel>> GetOrdersByCustomerAsync(int customerId);
    Task<IEnumerable<OrderReadModel>> GetOrdersByStatusAsync(string status);
}