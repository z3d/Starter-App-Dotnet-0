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
    private readonly IProductCommandService _commandService;

    public CreateProductCommandHandler(IProductCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateProductCommand");

        await _commandService.CreateProductAsync(
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.Stock
        );
    }

    public async Task<ProductDto> HandleAsync(
        CreateProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateProductCommand to return ProductDto");

        var createdProduct = await _commandService.CreateProductAsync(
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.Stock
        );

        return new ProductDto
        {
            Id = createdProduct.Id,
            Name = createdProduct.Name,
            Description = createdProduct.Description,
            Price = createdProduct.Price.Amount,
            Currency = createdProduct.Price.Currency,
            Stock = createdProduct.Stock,
            LastUpdated = createdProduct.LastUpdated
        };
    }
}



