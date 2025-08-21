using StarterApp.Api.Application.Interfaces;
using Serilog;

namespace StarterApp.Api.Application.Commands;

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

    public DeleteProductCommandHandler(IProductCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling DeleteProductCommand for product {Id}", command.Id);

        var deleted = await _commandService.DeleteProductAsync(command.Id);

        if (!deleted)
            throw new KeyNotFoundException($"Product with ID {command.Id} not found");
    }

    public async Task<bool> HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling DeleteProductCommand for product {Id}", command.Id);

        return await _commandService.DeleteProductAsync(command.Id);
    }
}



