using Microsoft.EntityFrameworkCore.Storage;

namespace StarterApp.Api.Application.Commands;

[FeatureToggle("order-placement")]
public class CreateOrderCommand : ICommand, IRequest<OrderDto>, IOwnerAuthorizedMutation
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
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public CreateOrderCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<CreateOrderCommandHandler> logger, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<OrderDto> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating order for customer {CustomerId} with EF Core", command.CustomerId);

        EnsureNoDuplicateProducts(command);
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (customer == null)
            throw new EntityNotFoundException($"Customer with ID {command.CustomerId} was not found");

        _ownerOnlyPolicy.Authorize(customer.OwnerSubject, customer.TenantId);

        // The stock update and the insert share one transaction, which must run inside the execution strategy; skipped on the non-relational test provider.
        var orderId = Guid.CreateVersion7();
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        Order? savedOrder = null;

        await strategy.ExecuteAsync(cancellationToken, async ct =>
        {
            // A prior failed attempt's tracked entities would be inserted again on this pass.
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
                var order = new Order(orderId, command.CustomerId, ownerScope.OwnerSubject, ownerScope.TenantId, _timeProvider.GetUtcNow());
                foreach (var itemCommand in command.Items)
                {
                    var product = await ReserveStockAsync(itemCommand, ownerScope, ct);
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

        _logger.LogInformation("Created order with ID: {OrderId}", savedOrder!.Id);

        // Evict the cached product read model so it does not serve the pre-reservation stock.
        foreach (var productId in command.Items.Select(item => item.ProductId).Distinct())
            await _cacheInvalidator.InvalidateProductAsync(productId, cancellationToken);

        return OrderMapper.ToDto(savedOrder);
    }

    // Relational: atomic UPDATE ... WHERE Stock >= qty. InMemory (tests) cannot ExecuteUpdate, so it falls back to read-modify-write.
    private Task<Product> ReserveStockAsync(CreateOrderItemCommand itemCommand, OwnerScope ownerScope, CancellationToken cancellationToken)
    {
        return _dbContext.Database.IsRelational()
            ? ReserveStockWithAtomicUpdateAsync(itemCommand, ownerScope, cancellationToken)
            : ReserveStockInMemoryAsync(itemCommand, cancellationToken);
    }

    private async Task<Product> ReserveStockWithAtomicUpdateAsync(
        CreateOrderItemCommand itemCommand,
        OwnerScope ownerScope,
        CancellationToken cancellationToken)
    {
        // AsNoTracking: the ExecuteUpdate below bypasses the tracker, so a tracked entity would hold stale stock.
        var product = await _dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == itemCommand.ProductId, cancellationToken);

        if (product == null)
            throw new EntityNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

        _ownerOnlyPolicy.Authorize(product.OwnerSubject, product.TenantId);

        // Atomic stock reservation — WHERE Stock >= @qty prevents concurrent overselling
        var updatedRows = await _dbContext.Products
            .Where(p =>
                p.Id == itemCommand.ProductId &&
                p.OwnerSubject == ownerScope.OwnerSubject &&
                p.TenantId == ownerScope.TenantId &&
                p.Stock >= itemCommand.Quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Stock, p => p.Stock - itemCommand.Quantity)
                    .SetProperty(p => p.LastUpdated, _ => _timeProvider.GetUtcNow()),
                cancellationToken);

        if (updatedRows == 0)
            throw new DomainRuleException(
                $"Insufficient stock for product '{product.Name}'. Available stock changed before the order could be placed.");

        return product;
    }

    private async Task<Product> ReserveStockInMemoryAsync(
        CreateOrderItemCommand itemCommand,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.FindAsync([itemCommand.ProductId], cancellationToken);
        if (product == null)
            throw new EntityNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

        _ownerOnlyPolicy.Authorize(product.OwnerSubject, product.TenantId);

        if (product.Stock < itemCommand.Quantity)
            throw new DomainRuleException(
                $"Insufficient stock for product '{product.Name}'. Available stock changed before the order could be placed.");

        product.UpdateStock(-itemCommand.Quantity);
        return product;
    }

    private static void EnsureNoDuplicateProducts(CreateOrderCommand command)
    {
        // Mirrors the validator: a handler invoked directly must not dereference a null item.
        if (command.Items.Any(item => item is null))
            throw new ValidationException([new ValidationError(nameof(command.Items), "Order item must not be null")]);

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
