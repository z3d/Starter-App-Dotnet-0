using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Domain.ValueObjects;
using Serilog;

namespace StarterApp.Api.Application.Commands;

public class UpdateProductCommand : ICommand, IRequest<ProductDto?>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Stock { get; set; }
}

public class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>,
                                         IRequestHandler<UpdateProductCommand, ProductDto?>
{
    private readonly IProductCommandService _commandService;

    public UpdateProductCommandHandler(IProductCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateProductCommand for product {Id}", command.Id);

        var result = await _commandService.UpdateProductAsync(
            command.Id,
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.Stock
        );

        if (result == null)
            throw new KeyNotFoundException($"Product with ID {command.Id} not found");
    }

    public async Task<ProductDto?> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateProductCommand for product {Id}", command.Id);

        var updatedProduct = await _commandService.UpdateProductAsync(
            command.Id,
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.Stock
        );

        if (updatedProduct == null)
            return null;

        return new ProductDto
        {
            Id = updatedProduct.Id,
            Name = updatedProduct.Name,
            Description = updatedProduct.Description,
            Price = updatedProduct.Price.Amount,
            Currency = updatedProduct.Price.Currency,
            Stock = updatedProduct.Stock,
            LastUpdated = updatedProduct.LastUpdated
        };
    }
}



