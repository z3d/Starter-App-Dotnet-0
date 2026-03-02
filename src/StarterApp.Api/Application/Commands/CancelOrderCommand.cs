namespace StarterApp.Api.Application.Commands;

public class CancelOrderCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;

    public CancelOrderCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderDto> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CancelOrderCommand to return OrderDto for order {OrderId}", command.OrderId);

        var orderEntity = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == command.OrderId);
        if (orderEntity == null)
        {
            Log.Warning("Order {OrderId} not found for cancellation", command.OrderId);
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        var orderItems = await _dbContext.OrderItems.AsNoTracking()
            .Where(oi => oi.OrderId == command.OrderId).ToListAsync();

        var order = Order.Reconstitute(orderEntity.Id, orderEntity.CustomerId, orderEntity.OrderDate, orderEntity.Status, orderEntity.LastUpdated, orderItems);
        order.Cancel();
        _dbContext.Orders.Update(order);
        await _dbContext.SaveChangesAsync();

        return OrderMapper.ToDto(order);
    }
}
