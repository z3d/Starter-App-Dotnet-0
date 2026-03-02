namespace StarterApp.Api.Application.Commands;

public class UpdateOrderStatusCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOrderRepository _orderRepository;

    public UpdateOrderStatusCommandHandler(ApplicationDbContext dbContext, IOrderRepository orderRepository)
    {
        _dbContext = dbContext;
        _orderRepository = orderRepository;
    }

    public async Task<OrderDto> HandleAsync(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateOrderStatusCommand to return OrderDto for order {OrderId}", command.OrderId);

        var status = Enum.Parse<OrderStatus>(command.Status);

        var order = await _orderRepository.LoadWithItemsAsync(command.OrderId);
        if (order == null)
        {
            Log.Warning("Order {OrderId} not found for status update", command.OrderId);
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        order.UpdateStatus(status);
        _dbContext.Orders.Update(order);
        await _dbContext.SaveChangesAsync();

        return OrderMapper.ToDto(order);
    }
}
