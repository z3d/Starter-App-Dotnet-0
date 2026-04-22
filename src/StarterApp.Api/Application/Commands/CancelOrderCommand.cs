namespace StarterApp.Api.Application.Commands;

public class CancelOrderCommand : ICommand, IRequest<OrderDto>
{
    public Guid OrderId { get; set; }
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

        var order = await _dbContext.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order == null)
        {
            Log.Warning("Order {OrderId} not found for cancellation", command.OrderId);
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");
        }

        order.Cancel();

        // Restore stock for each item in the cancelled order
        foreach (var item in order.Items)
        {
            var product = await _dbContext.Products.FindAsync([item.ProductId], cancellationToken);
            if (product == null)
            {
                Log.Warning("Product {ProductId} no longer exists; cannot restore {Quantity} units of stock for order {OrderId}",
                    item.ProductId, item.Quantity, order.Id);
                continue;
            }

            product.UpdateStock(item.Quantity);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return OrderMapper.ToDto(order);
    }
}
