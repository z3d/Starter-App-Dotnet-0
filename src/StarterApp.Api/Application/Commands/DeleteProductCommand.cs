using StarterApp.Api.Data;
using Serilog;

namespace StarterApp.Api.Application.Commands;

public class DeleteProductCommand : ICommand, IRequest<bool>
{
    public int Id { get; }

    public DeleteProductCommand(int id)
    {
        Id = id;
    }
}

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, bool>
{
    private readonly ApplicationDbContext _dbContext;

    public DeleteProductCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling DeleteProductCommand for product {Id}", command.Id);

        Log.Information("Deleting product {Id} with EF Core", command.Id);

        var product = await _dbContext.Products.FindAsync([command.Id], cancellationToken);
        if (product == null)
        {
            Log.Warning("Product {Id} not found for deletion", command.Id);
            return false;
        }

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("Deleted product with ID: {ProductId}", command.Id);
        return true;
    }
}



