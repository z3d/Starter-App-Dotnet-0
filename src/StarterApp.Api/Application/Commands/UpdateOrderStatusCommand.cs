namespace StarterApp.Api.Application.Commands;

public class UpdateOrderStatusCommand : ICommand, IRequest<OrderDto>, IOwnerAuthorizedMutation
{
    public Guid OrderId { get; set; }
    public OrderStatus? Status { get; set; }
}

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<UpdateOrderStatusCommandHandler> _logger;

    public UpdateOrderStatusCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<UpdateOrderStatusCommandHandler> logger)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<OrderDto> HandleAsync(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling UpdateOrderStatusCommand to return OrderDto for order {OrderId}", command.OrderId);

        var order = await _dbContext.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found for status update", command.OrderId);
            throw new EntityNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        _ownerOnlyPolicy.Authorize(order.OwnerSubject, order.TenantId);

        var status = command.Status!.Value;
        if (status == OrderStatus.Cancelled)
            await OrderCancellationService.CancelAndRestoreStockAsync(_dbContext, order, _logger, cancellationToken);
        else
            ApplyLifecycleTransition(order, status);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Only cancellation touches stock; evict the cached product read model so it does not serve the pre-restore value.
        if (status == OrderStatus.Cancelled)
        {
            foreach (var productId in order.Items.Select(item => item.ProductId).Distinct())
                await _cacheInvalidator.InvalidateProductAsync(productId, cancellationToken);
        }

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
