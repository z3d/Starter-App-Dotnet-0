namespace StarterApp.Api.Application.Commands;

public class UpdateOrderStatusCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;

    public UpdateOrderStatusCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderDto> HandleAsync(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateOrderStatusCommand to return OrderDto for order {OrderId}", command.OrderId);

        var status = Enum.Parse<OrderStatus>(command.Status);

        var orderEntity = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == command.OrderId);
        if (orderEntity == null)
        {
            Log.Warning("Order {OrderId} not found for status update", command.OrderId);
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        var orderItems = await _dbContext.OrderItems.AsNoTracking()
            .Where(oi => oi.OrderId == command.OrderId).ToListAsync();

        var order = Order.Reconstitute(orderEntity.Id, orderEntity.CustomerId, orderEntity.OrderDate, orderEntity.Status, orderEntity.LastUpdated, orderItems);
        order.UpdateStatus(status);
        _dbContext.Orders.Update(order);
        await _dbContext.SaveChangesAsync();

        return OrderMapper.ToDto(order);
    }
}
