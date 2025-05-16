using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearning.Domain.Entities;
using DockerLearning.Domain.Interfaces;
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
    private readonly IProductRepository _productRepository;

    public CreateProductCommandHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = Product.Create(
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.Stock
        );

        await _productRepository.AddAsync(product);
    }

    async Task<ProductDto> IRequestHandler<CreateProductCommand, ProductDto>.Handle(
        CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = Product.Create(
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency),
            command.Stock
        );

        var createdProduct = await _productRepository.AddAsync(product);

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