namespace StarterApp.Api.Application.Commands;

public class DeleteProductCommand : ICommand, IRequest
{
    public int Id { get; }

    public DeleteProductCommand(int id)
    {
        Id = id;
    }
}

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand>
{
    private readonly ApplicationDbContext _dbContext;

    public DeleteProductCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling DeleteProductCommand for product {Id}", command.Id);

        var product = await _dbContext.Products.FindAsync([command.Id], cancellationToken);
        if (product == null)
        {
            Log.Warning("Product {Id} not found for deletion", command.Id);
            throw new KeyNotFoundException($"Product with ID {command.Id} not found");
        }

        var hasOrderItems = await _dbContext.OrderItems.AnyAsync(oi => oi.ProductId == command.Id, cancellationToken);
        if (hasOrderItems)
        {
            Log.Warning("Product {Id} cannot be deleted because it has existing order items", command.Id);
            throw new InvalidOperationException($"Cannot delete product '{product.Name}' because it is referenced by existing orders");
        }

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("Deleted product with ID: {ProductId}", command.Id);
    }
}
