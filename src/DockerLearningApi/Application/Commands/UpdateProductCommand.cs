using DockerLearningApi.Application.Interfaces;
using DockerLearningApi.Domain.Interfaces;
using DockerLearningApi.Domain.ValueObjects;
using MediatR;

namespace DockerLearningApi.Application.Commands;

public class UpdateProductCommand : ICommand, IRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Stock { get; set; }
}

public class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>, 
                                          IRequestHandler<UpdateProductCommand>
{
    private readonly IProductRepository _productRepository;

    public UpdateProductCommandHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(command.Id);
        if (product == null)
            throw new KeyNotFoundException($"Product with ID {command.Id} not found");

        product.UpdateDetails(
            command.Name,
            command.Description,
            Money.Create(command.Price, command.Currency)
        );

        if (product.Stock != command.Stock)
        {
            var diff = command.Stock - product.Stock;
            product.UpdateStock(diff);
        }

        await _productRepository.UpdateAsync(product);
    }

    async Task IRequestHandler<UpdateProductCommand>.Handle(
        UpdateProductCommand command, CancellationToken cancellationToken)
    {
        await Handle(command, cancellationToken);
    }
}