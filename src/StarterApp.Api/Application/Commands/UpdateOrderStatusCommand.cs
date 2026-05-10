namespace StarterApp.Api.Application.Commands;

public class UpdateOrderStatusCommand : ICommand, IRequest<OrderDto>
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public UpdateOrderStatusCommandHandler(ApplicationDbContext dbContext, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _dbContext = dbContext;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<OrderDto> HandleAsync(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateOrderStatusCommand to return OrderDto for order {OrderId}", command.OrderId);

        var status = Enum.Parse<OrderStatus>(command.Status, ignoreCase: true);

        var order = await _dbContext.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order == null)
        {
            Log.Warning("Order {OrderId} not found for status update", command.OrderId);
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        _ownerOnlyPolicy.Authorize(order.OwnerSubject, order.TenantId);

        if (status == OrderStatus.Cancelled)
            await OrderCancellationService.CancelAndRestoreStockAsync(_dbContext, order, cancellationToken);
        else
            order.UpdateStatus(status);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return OrderMapper.ToDto(order);
    }
}
