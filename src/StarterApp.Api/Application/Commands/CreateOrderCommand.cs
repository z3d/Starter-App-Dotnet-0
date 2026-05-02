using Microsoft.EntityFrameworkCore.Storage;

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

        var customerExists = await _dbContext.Customers.AnyAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer with ID {command.CustomerId} was not found");

        // Atomic stock-reservation + order-insert must share one transaction (ExecuteUpdate bypasses
        // the SaveChanges transaction). With EnableRetryOnFailure, user transactions must be wrapped
        // in the execution strategy so transient SQL faults can retry the whole unit of work.
        // On InMemory (tests) the strategy is a no-op; the transaction is skipped via IsRelational —
        // same outer code path runs safely for both providers.
        var orderId = Guid.CreateVersion7();
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        Order? savedOrder = null;

        await strategy.ExecuteAsync(cancellationToken, async ct =>
        {
            // Clear tracker so a prior failed attempt's tracked entities do not leak into this retry —
            // otherwise two Added orders would be inserted on a second pass.
            _dbContext.ChangeTracker.Clear();

            var committedOrder = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (committedOrder != null)
            {
                savedOrder = committedOrder;
                return;
            }

            IDbContextTransaction? transaction = null;
            if (_dbContext.Database.IsRelational())
                transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            try
            {
                var order = new Order(orderId, command.CustomerId);
                foreach (var itemCommand in command.Items)
                {
                    var product = await ReserveStockAsync(itemCommand, ct);
                    order.AddItem(
                        itemCommand.ProductId,
                        product.Name,
                        itemCommand.Quantity,
                        product.Price,
                        OrderItem.DefaultGstRate);
                }

                _dbContext.Orders.Add(order);
                await _dbContext.SaveChangesAsync(ct);

                if (transaction != null)
                    await transaction.CommitAsync(ct);

                savedOrder = order;
            }
            finally
            {
                if (transaction != null)
                    await transaction.DisposeAsync();
            }
        });

        Log.Information("Created order with ID: {OrderId}", savedOrder!.Id);
        return OrderMapper.ToDto(savedOrder);
    }

    // Single provider branch: relational uses atomic SQL (UPDATE ... WHERE Stock >= qty) to prevent
    // overselling under concurrency; InMemory (tests only) falls back to tracked read-modify-write
    // because ExecuteUpdateAsync is not supported on that provider.
    private Task<Product> ReserveStockAsync(CreateOrderItemCommand itemCommand, CancellationToken cancellationToken)
    {
        return _dbContext.Database.IsRelational()
            ? ReserveStockWithAtomicUpdateAsync(itemCommand, cancellationToken)
            : ReserveStockInMemoryAsync(itemCommand, cancellationToken);
    }

    private async Task<Product> ReserveStockWithAtomicUpdateAsync(
        CreateOrderItemCommand itemCommand,
        CancellationToken cancellationToken)
    {
        // Load product first to verify existence and get catalog details (name, price).
        // AsNoTracking because ExecuteUpdateAsync below bypasses the change tracker —
        // a tracked entity would hold a stale Stock snapshot after the direct SQL update.
        var product = await _dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == itemCommand.ProductId, cancellationToken);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

        // Atomic stock reservation — WHERE Stock >= @qty prevents concurrent overselling
        var updatedRows = await _dbContext.Products
            .Where(p => p.Id == itemCommand.ProductId && p.Stock >= itemCommand.Quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Stock, p => p.Stock - itemCommand.Quantity)
                    .SetProperty(p => p.LastUpdated, _ => DateTimeOffset.UtcNow),
                cancellationToken);

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

        throw new ValidationException(
        [
            new ValidationError(
                nameof(command.Items),
                $"Each product may only appear once per order. Duplicate product IDs: {string.Join(", ", duplicateProductIds)}")
        ]);
    }
}
