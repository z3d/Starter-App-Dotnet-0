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

        foreach (var itemCommand in command.Items)
        {
            var product = await _dbContext.Products.FindAsync([itemCommand.ProductId], cancellationToken);
            if (product == null)
                throw new KeyNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

            if (product.Stock < itemCommand.Quantity)
                throw new InvalidOperationException(
                    $"Insufficient stock for product '{product.Name}'. Available: {product.Stock}, Requested: {itemCommand.Quantity}");

            product.UpdateStock(-itemCommand.Quantity);

            order.AddItem(
                itemCommand.ProductId,
                product.Name,
                itemCommand.Quantity,
                Money.Create(itemCommand.UnitPriceExcludingGst, itemCommand.Currency),
                itemCommand.GstRate
            );
        }

        // Single save — EF Core persists order + items atomically and sets OrderId via FK
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("Created order with ID: {OrderId}", order.Id);
        return OrderMapper.ToDto(order);
    }
}
