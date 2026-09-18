namespace StarterApp.Api.Application.Commands;

public class CancelOrderCommand : ICommand, IRequest<OrderDto>, IOwnerAuthorizedMutation
{
    public Guid OrderId { get; set; }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<CancelOrderCommandHandler> _logger;

    public CancelOrderCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<CancelOrderCommandHandler> logger)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<OrderDto> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling CancelOrderCommand to return OrderDto for order {OrderId}", command.OrderId);

        var order = await _dbContext.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found for cancellation", command.OrderId);
            throw new EntityNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        _ownerOnlyPolicy.Authorize(order.OwnerSubject, order.TenantId);

        await OrderCancellationService.CancelAndRestoreStockAsync(_dbContext, order, _logger, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Cancellation restored stock for every line item; purge the cached product read model so
        // a subsequent GetProductByIdQuery does not serve stale (pre-restore) stock.
        foreach (var productId in order.Items.Select(item => item.ProductId).Distinct())
            await _cacheInvalidator.InvalidateProductAsync(productId, cancellationToken);

        return OrderMapper.ToDto(order);
    }
}
