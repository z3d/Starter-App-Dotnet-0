namespace StarterApp.Api.Application.Commands;

public class UpdateProductCommand : ICommand, IRequest<ProductDto?>
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Stock { get; set; }
}

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, ProductDto?>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheInvalidator _cacheInvalidator;

    public UpdateProductCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task<ProductDto?> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateProductCommand for product {Id}", command.Id);

        Log.Information("Updating product {Id} with EF Core", command.Id);

        var product = await _dbContext.Products.FindAsync([command.Id], cancellationToken);
        if (product == null)
        {
            Log.Warning("Product {Id} not found for update", command.Id);
            throw new KeyNotFoundException($"Product with ID {command.Id} not found");
        }

        var price = Money.Create(command.Price!.Value, command.Currency!);
        product.UpdateDetails(command.Name!, command.Description, price);

        // Update stock separately
        var stockDifference = command.Stock!.Value - product.Stock;
        if (stockDifference != 0)
        {
            product.UpdateStock(stockDifference);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _cacheInvalidator.InvalidateProductAsync(product.Id, cancellationToken);

        Log.Information("Updated product with ID: {ProductId}", product.Id);

        // Map to DTO and return
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price.Amount,
            Currency = product.Price.Currency,
            Stock = product.Stock,
            LastUpdated = product.LastUpdated
        };
    }
}


