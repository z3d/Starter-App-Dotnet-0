namespace StarterApp.Api.Application.Commands;

public interface IOrderCommandService
{
    Task<Order> CreateOrderAsync(int customerId, List<CreateOrderItemCommand> items);
    Task<Order?> UpdateOrderStatusAsync(int orderId, OrderStatus status);
    Task<Order?> CancelOrderAsync(int orderId);
}



