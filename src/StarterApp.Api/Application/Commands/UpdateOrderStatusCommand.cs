namespace StarterApp.Api.Application.Commands;

public class UpdateOrderStatusCommand : ICommand, IRequest<OrderDto>
{
    public Guid OrderId { get; set; }
    public OrderStatus? Status { get; set; }
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

        var order = await _dbContext.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order == null)
        {
            Log.Warning("Order {OrderId} not found for status update", command.OrderId);
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        _ownerOnlyPolicy.Authorize(order.OwnerSubject, order.TenantId);

        var status = command.Status!.Value;
        if (status == OrderStatus.Cancelled)
            await OrderCancellationService.CancelAndRestoreStockAsync(_dbContext, order, cancellationToken);
        else
            ApplyLifecycleTransition(order, status);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return OrderMapper.ToDto(order);
    }

    private static void ApplyLifecycleTransition(Order order, OrderStatus status)
    {
        switch (status)
        {
            case OrderStatus.Confirmed:
                order.Confirm();
                break;
            case OrderStatus.Processing:
                order.StartProcessing();
                break;
            case OrderStatus.Shipped:
                order.Ship();
                break;
            case OrderStatus.Delivered:
                order.Deliver();
                break;
            default:
                order.UpdateStatus(status);
                break;
        }
    }
}
