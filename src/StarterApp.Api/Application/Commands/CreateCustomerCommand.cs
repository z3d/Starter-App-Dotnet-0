namespace StarterApp.Api.Application.Commands;

public class CreateCustomerCommand : ICommand, IRequest<CustomerDto>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheInvalidator _cacheInvalidator;

    public CreateCustomerCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task<CustomerDto> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateCustomerCommand to return CustomerDto");

        Log.Information("Creating customer {Name} with EF Core", command.Name);

        var email = Email.Create(command.Email);
        await EnsureEmailIsUniqueAsync(email.Value, cancellationToken);

        var customer = new Customer(command.Name, email);

        _dbContext.Customers.Add(customer);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation("IX_Customers_Email"))
        {
            throw new InvalidOperationException($"A customer with email '{email.Value}' already exists", ex);
        }

        await _cacheInvalidator.InvalidateCustomerAsync(customer.Id, cancellationToken);
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

    private async Task EnsureEmailIsUniqueAsync(string email, CancellationToken cancellationToken)
    {
        var emailExists = await _dbContext.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Email.Value == email, cancellationToken);

        if (emailExists)
            throw new InvalidOperationException($"A customer with email '{email}' already exists");
    }
}


