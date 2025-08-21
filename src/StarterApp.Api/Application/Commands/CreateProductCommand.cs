using StarterApp.Api.Data;

namespace StarterApp.Api.Application.Commands;

public class CreateProductCommand : ICommand, IRequest<ProductDto>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Stock { get; set; }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly ApplicationDbContext _dbContext;

    public CreateProductCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProductDto> HandleAsync(
        CreateProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateProductCommand to return ProductDto");

        Log.Information("Creating product {Name} with EF Core", command.Name);

        var price = Money.Create(command.Price, command.Currency);
        var product = new Product(command.Name, command.Description, price, command.Stock);

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("Created new product with ID: {ProductId}", product.Id);

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



