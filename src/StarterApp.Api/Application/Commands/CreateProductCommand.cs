namespace StarterApp.Api.Application.Commands;

public class CreateProductCommand : ICommand, IRequest<ProductDto>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    // Price, Currency and Stock are nullable so an absent JSON field is distinguishable from an
    // explicit value: a non-nullable decimal deserializes a missing "price" to 0 and the product
    // silently becomes free, and a defaulted currency silently prices it in USD while the update
    // endpoint rejects the same body. The validator rejects null, so handlers may dereference.
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Stock { get; set; }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(ApplicationDbContext dbContext, IOwnerOnlyPolicy ownerOnlyPolicy, ILogger<CreateProductCommandHandler> logger)
    {
        _dbContext = dbContext;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _logger = logger;
    }

    public async Task<ProductDto> HandleAsync(
        CreateProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling CreateProductCommand to return ProductDto");

        _logger.LogInformation("Creating product {Name} with EF Core", command.Name);

        var price = Money.Create(command.Price!.Value, command.Currency!);
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();
        var product = new Product(command.Name, command.Description, price, command.Stock!.Value, ownerScope.OwnerSubject, ownerScope.TenantId);

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created new product with ID: {ProductId}", product.Id);

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

