namespace StarterApp.Api.Application.Commands;

public class UpdateCustomerCommand : ICommand, IRequest<CustomerDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class UpdateCustomerCommandHandler : ICommandHandler<UpdateCustomerCommand>,
                                          IRequestHandler<UpdateCustomerCommand, CustomerDto>
{
    private readonly ICustomerCommandService _commandService;

    public UpdateCustomerCommandHandler(ICustomerCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateCustomerCommand for Customer {CustomerId}", command.Id);

        await _commandService.UpdateCustomerAsync(
            command.Id,
            command.Name,
            Email.Create(command.Email)
        );
    }

    async Task<CustomerDto> IRequestHandler<UpdateCustomerCommand, CustomerDto>.Handle(
        UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateCustomerCommand to return CustomerDto for Customer {CustomerId}", command.Id);

        var updatedCustomer = await _commandService.UpdateCustomerAsync(
            command.Id,
            command.Name,
            Email.Create(command.Email)
        );

        if (updatedCustomer == null)
            throw new KeyNotFoundException($"Customer with ID {command.Id} not found");

        return new CustomerDto
        {
            Id = updatedCustomer.Id,
            Name = updatedCustomer.Name,
            Email = updatedCustomer.Email.Value,
            DateCreated = updatedCustomer.DateCreated,
            IsActive = updatedCustomer.IsActive
        };
    }
}
