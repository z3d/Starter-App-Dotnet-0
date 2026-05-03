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
    private readonly ICacheInvalidator _cacheInvalidator;

    public UpdateCustomerCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
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
        await EnsureEmailIsUniqueAsync(email.Value, command.Id, cancellationToken);

        customer.UpdateDetails(command.Name, email);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation("IX_Customers_Email"))
        {
            throw new InvalidOperationException("A customer with that email already exists", ex);
        }

        await _cacheInvalidator.InvalidateCustomerAsync(customer.Id, cancellationToken);
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

    private async Task EnsureEmailIsUniqueAsync(string email, int customerId, CancellationToken cancellationToken)
    {
        var emailExists = await _dbContext.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id != customerId && customer.Email.Value == email, cancellationToken);

        if (emailExists)
            throw new InvalidOperationException("A customer with that email already exists");
    }
}

