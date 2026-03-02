namespace StarterApp.Api.Application.Commands;

public class CreateOrderCommand : ICommand, IRequest<OrderDto>
{
    public int CustomerId { get; set; }
    public List<CreateOrderItemCommand> Items { get; set; } = [];
}

public class CreateOrderItemCommand
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceExcludingGst { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal GstRate { get; set; } = OrderItem.DefaultGstRate;
}

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;

    public CreateOrderCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderDto> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Creating order for customer {CustomerId} with EF Core", command.CustomerId);

        // Validate that customer exists
        var customerExists = await _dbContext.Customers.AnyAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer with ID {command.CustomerId} was not found");

        var order = new Order(command.CustomerId);

        // Save order header to get the generated ID
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Create all order items
        var orderItems = new List<OrderItem>();
        foreach (var itemCommand in command.Items)
        {
            var product = await _dbContext.Products.FindAsync([itemCommand.ProductId], cancellationToken);
            if (product == null)
                throw new KeyNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

            var orderItem = new OrderItem(
                order.Id,
                itemCommand.ProductId,
                product.Name,
                itemCommand.Quantity,
                Money.Create(itemCommand.UnitPriceExcludingGst, itemCommand.Currency),
                itemCommand.GstRate
            );

            _dbContext.OrderItems.Add(orderItem);
            orderItems.Add(orderItem);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Build the order in memory instead of reloading from DB
        var result = Order.Reconstitute(order.Id, order.CustomerId, order.OrderDate, order.Status, order.LastUpdated, orderItems);

        Log.Information("Created order with ID: {OrderId}", order.Id);
        return OrderMapper.ToDto(result);
    }
}
