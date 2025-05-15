using DockerLearningApi.Application.Interfaces;
using DockerLearningApi.Domain.Interfaces;
using MediatR;

namespace DockerLearningApi.Application.Commands;

public class DeleteProductCommand : ICommand, IRequest
{
    public int Id { get; set; }

    public DeleteProductCommand(int id)
    {
        Id = id;
    }
}

public class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>, 
                                          IRequestHandler<DeleteProductCommand>
{
    private readonly IProductRepository _productRepository;

    public DeleteProductCommandHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        await _productRepository.DeleteAsync(command.Id);
    }

    async Task IRequestHandler<DeleteProductCommand>.Handle(
        DeleteProductCommand command, CancellationToken cancellationToken)
    {
        await Handle(command, cancellationToken);
    }
}