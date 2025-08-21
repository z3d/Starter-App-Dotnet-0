namespace StarterApp.Api.Application.Commands;

public class CreateCustomerCommand : ICommand, IRequest<CustomerDto>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class CreateCustomerCommandHandler : ICommandHandler<CreateCustomerCommand>,
                                          IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly ICustomerCommandService _commandService;

    public CreateCustomerCommandHandler(ICustomerCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateCustomerCommand");

        await _commandService.CreateCustomerAsync(
            command.Name,
            Email.Create(command.Email)
        );
    }

    async Task<CustomerDto> IRequestHandler<CreateCustomerCommand, CustomerDto>.Handle(
        CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateCustomerCommand to return CustomerDto");

        var createdCustomer = await _commandService.CreateCustomerAsync(
            command.Name,
            Email.Create(command.Email)
        );

        return new CustomerDto
        {
            Id = createdCustomer.Id,
            Name = createdCustomer.Name,
            Email = createdCustomer.Email.Value,
            DateCreated = createdCustomer.DateCreated,
            IsActive = createdCustomer.IsActive
        };
    }
}
