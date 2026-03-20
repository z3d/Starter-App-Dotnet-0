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

        EnsureNoDuplicateProducts(command);

        // Validate that customer exists
        var customerExists = await _dbContext.Customers.AnyAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer with ID {command.CustomerId} was not found");

        var useRelationalStockUpdates = _dbContext.Database.IsRelational();
        await using var transaction = useRelationalStockUpdates
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var order = new Order(command.CustomerId);

        foreach (var itemCommand in command.Items)
        {
            var product = useRelationalStockUpdates
                ? await ReserveStockWithAtomicUpdateAsync(itemCommand, cancellationToken)
                : await ReserveStockInMemoryAsync(itemCommand, cancellationToken);

            order.AddItem(
                itemCommand.ProductId,
                product.Name,
                itemCommand.Quantity,
                product.Price,
                OrderItem.DefaultGstRate
            );
        }

        // Single save — EF Core persists order + items atomically and sets OrderId via FK
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);

        Log.Information("Created order with ID: {OrderId}", order.Id);
        return OrderMapper.ToDto(order);
    }

    private async Task<Product> ReserveStockWithAtomicUpdateAsync(
        CreateOrderItemCommand itemCommand,
        CancellationToken cancellationToken)
    {
        var updatedRows = await _dbContext.Products
            .Where(p => p.Id == itemCommand.ProductId && p.Stock >= itemCommand.Quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Stock, p => p.Stock - itemCommand.Quantity)
                    .SetProperty(p => p.LastUpdated, _ => DateTime.UtcNow),
                cancellationToken);

        var product = await _dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == itemCommand.ProductId, cancellationToken);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

        if (updatedRows == 0)
            throw new InvalidOperationException(
                $"Insufficient stock for product '{product.Name}'. Available stock changed before the order could be placed.");

        return product;
    }

    private async Task<Product> ReserveStockInMemoryAsync(
        CreateOrderItemCommand itemCommand,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.FindAsync([itemCommand.ProductId], cancellationToken);
        if (product == null)
            throw new KeyNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

        if (product.Stock < itemCommand.Quantity)
            throw new InvalidOperationException(
                $"Insufficient stock for product '{product.Name}'. Available stock changed before the order could be placed.");

        product.UpdateStock(-itemCommand.Quantity);
        return product;
    }

    private static void EnsureNoDuplicateProducts(CreateOrderCommand command)
    {
        var duplicateProductIds = command.Items
            .GroupBy(item => item.ProductId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateProductIds.Count == 0)
            return;

        throw new StarterApp.Api.Infrastructure.Validation.ValidationException(
        [
            new StarterApp.Api.Infrastructure.Validation.ValidationError(
                nameof(command.Items),
                $"Each product may only appear once per order. Duplicate product IDs: {string.Join(", ", duplicateProductIds)}")
        ]);
    }
}
