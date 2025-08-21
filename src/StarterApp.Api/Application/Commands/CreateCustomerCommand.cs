using StarterApp.Api.Data;

namespace StarterApp.Api.Application.Commands;

public class CreateCustomerCommand : ICommand, IRequest<CustomerDto>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly ApplicationDbContext _dbContext;

    public CreateCustomerCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CustomerDto> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateCustomerCommand to return CustomerDto");

        Log.Information("Creating customer {Name} with EF Core", command.Name);
        
        var email = Email.Create(command.Email);
        var customer = new Customer(command.Name, email);

        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("Created new customer with ID: {CustomerId}", customer.Id);

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



