using StarterApp.Api.Data;

namespace StarterApp.Api.Application.Commands;

public class UpdateCustomerCommand : ICommand, IRequest<CustomerDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand, CustomerDto>
{
    private readonly ApplicationDbContext _dbContext;

    public UpdateCustomerCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CustomerDto> HandleAsync(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateCustomerCommand to return CustomerDto for Customer {CustomerId}", command.Id);

        Log.Information("Updating customer {Id} with EF Core", command.Id);

        var customer = await _dbContext.Customers.FindAsync([command.Id], cancellationToken);
        if (customer == null)
        {
            Log.Warning("Customer {Id} not found for update", command.Id);
            throw new KeyNotFoundException($"Customer with ID {command.Id} not found");
        }

        var email = Email.Create(command.Email);
        customer.UpdateDetails(command.Name, email);

        await _dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("Updated customer with ID: {CustomerId}", customer.Id);

        // Map to DTO and return
        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email.Value,
            DateCreated = customer.DateCreated,
            IsActive = customer.IsActive
        };
    }
}



