namespace StarterApp.Api.Application.Commands;

public class CancelOrderCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOrderRepository _orderRepository;

    public CancelOrderCommandHandler(ApplicationDbContext dbContext, IOrderRepository orderRepository)
    {
        _dbContext = dbContext;
        _orderRepository = orderRepository;
    }

    public async Task<OrderDto> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CancelOrderCommand to return OrderDto for order {OrderId}", command.OrderId);

        var order = await _orderRepository.LoadWithItemsAsync(command.OrderId);
        if (order == null)
        {
            Log.Warning("Order {OrderId} not found for cancellation", command.OrderId);
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        order.Cancel();
        _dbContext.Orders.Update(order);
        await _dbContext.SaveChangesAsync();

        return OrderMapper.ToDto(order);
    }
}
