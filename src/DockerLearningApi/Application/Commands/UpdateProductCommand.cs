using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearning.Domain.ValueObjects;
using MediatR;

namespace DockerLearningApi.Application.Commands;

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
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(
        IProductCommandService commandService,
        ILogger<UpdateProductCommandHandler> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    public async Task Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling UpdateProductCommand for product {Id}", command.Id);
        
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

    async Task<ProductDto?> IRequestHandler<UpdateProductCommand, ProductDto?>.Handle(
        UpdateProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling UpdateProductCommand for product {Id}", command.Id);
        
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