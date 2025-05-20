using DockerLearningApi.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DockerLearningApi.Application.Commands;

public class DeleteProductCommand : ICommand, IRequest<bool>
{
    public int Id { get; }

    public DeleteProductCommand(int id)
    {
        Id = id;
    }
}

public class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>, 
                                         IRequestHandler<DeleteProductCommand, bool>
{
    private readonly IProductCommandService _commandService;
    private readonly ILogger<DeleteProductCommandHandler> _logger;

    public DeleteProductCommandHandler(
        IProductCommandService commandService,
        ILogger<DeleteProductCommandHandler> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    public async Task Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling DeleteProductCommand for product {Id}", command.Id);
        
        var deleted = await _commandService.DeleteProductAsync(command.Id);
        
        if (!deleted)
            throw new KeyNotFoundException($"Product with ID {command.Id} not found");
    }

    async Task<bool> IRequestHandler<DeleteProductCommand, bool>.Handle(
        DeleteProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling DeleteProductCommand for product {Id}", command.Id);
        
        return await _commandService.DeleteProductAsync(command.Id);
    }
}