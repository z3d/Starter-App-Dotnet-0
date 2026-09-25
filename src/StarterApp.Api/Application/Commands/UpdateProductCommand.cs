namespace StarterApp.Api.Application.Commands;

public class UpdateProductCommand : ICommand, IRequest<ProductDto?>, IOwnerAuthorizedMutation
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
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<UpdateProductCommandHandler> logger)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<ProductDto?> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling UpdateProductCommand for product {Id}", command.Id);

        _logger.LogInformation("Updating product {Id} with EF Core", command.Id);

        var product = await _dbContext.Products.FindAsync([command.Id], cancellationToken);
        if (product == null)
        {
            _logger.LogWarning("Product {Id} not found for update", command.Id);
            throw new EntityNotFoundException($"Product with ID {command.Id} not found");
        }

        _ownerOnlyPolicy.Authorize(product.OwnerSubject, product.TenantId);

        var price = Money.Create(command.Price!.Value, command.Currency!);
        product.UpdateDetails(command.Name!, command.Description, price);

        var stockDifference = command.Stock!.Value - product.Stock;
        if (stockDifference != 0)
        {
            product.UpdateStock(stockDifference);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _cacheInvalidator.InvalidateProductAsync(product.Id, cancellationToken);

        _logger.LogInformation("Updated product with ID: {ProductId}", product.Id);

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

