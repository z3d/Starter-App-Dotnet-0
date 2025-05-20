using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearning.Domain.ValueObjects;
using MediatR;

namespace DockerLearningApi.Application.Commands;

public class CreateProductCommand : ICommand, IRequest<ProductDto>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Stock { get; set; }
}

public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand>, 
                                          IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductCommandService _commandService;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(
        IProductCommandService commandService,
        ILogger<CreateProductCommandHandler> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    public async Task Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling CreateProductCommand");
        
        await _commandService.CreateProductAsync(
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.Stock
        );
    }

    async Task<ProductDto> IRequestHandler<CreateProductCommand, ProductDto>.Handle(
        CreateProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling CreateProductCommand to return ProductDto");
        
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