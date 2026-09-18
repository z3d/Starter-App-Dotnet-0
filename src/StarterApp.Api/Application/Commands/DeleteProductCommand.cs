namespace StarterApp.Api.Application.Commands;

public class DeleteProductCommand : ICommand, IRequest<Unit>, IOwnerAuthorizedMutation
{
    public int Id { get; }

    public DeleteProductCommand(int id)
    {
        Id = id;
    }
}

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, Unit>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<DeleteProductCommandHandler> _logger;

    public DeleteProductCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<DeleteProductCommandHandler> logger)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<Unit> HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling DeleteProductCommand for product {Id}", command.Id);

        var product = await _dbContext.Products.FindAsync([command.Id], cancellationToken);
        if (product == null)
        {
            _logger.LogWarning("Product {Id} not found for deletion", command.Id);
            throw new EntityNotFoundException($"Product with ID {command.Id} not found");
        }

        _ownerOnlyPolicy.Authorize(product.OwnerSubject, product.TenantId);

        var hasOrderItems = await _dbContext.OrderItems.AnyAsync(oi => oi.ProductId == command.Id, cancellationToken);
        if (hasOrderItems)
        {
            _logger.LogWarning("Product {Id} cannot be deleted because it has existing order items", command.Id);
            throw new DomainRuleException($"Cannot delete product '{product.Name}' because it is referenced by existing orders");
        }

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _cacheInvalidator.InvalidateProductAsync(command.Id, cancellationToken);

        _logger.LogInformation("Deleted product with ID: {ProductId}", command.Id);
        return Unit.Value;
    }
}
