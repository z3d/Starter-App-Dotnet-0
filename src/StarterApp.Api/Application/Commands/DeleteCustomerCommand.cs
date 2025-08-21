namespace StarterApp.Api.Application.Commands;

public class DeleteCustomerCommand : ICommand, IRequest
{
    public int Id { get; set; }
}

public class DeleteCustomerCommandHandler : ICommandHandler<DeleteCustomerCommand>,
                                          IRequestHandler<DeleteCustomerCommand, Unit>
{
    private readonly ICustomerCommandService _commandService;

    public DeleteCustomerCommandHandler(ICustomerCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(DeleteCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling DeleteCustomerCommand for Customer {CustomerId}", command.Id);

        await _commandService.DeleteCustomerAsync(command.Id);
    }

    async Task<Unit> IRequestHandler<DeleteCustomerCommand, Unit>.Handle(DeleteCustomerCommand command, CancellationToken cancellationToken)
    {
        await Handle(command, cancellationToken);
        return Unit.Value;
    }
}
