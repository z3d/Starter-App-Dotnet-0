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
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public CreateCustomerCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<CustomerDto> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateCustomerCommand to return CustomerDto");

        Log.Information("Creating customer with EF Core");

        var email = Email.Create(command.Email);
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();
        await EnsureEmailIsUniqueAsync(email.Value, ownerScope, cancellationToken);

        var customer = new Customer(command.Name, email, ownerScope.OwnerSubject, ownerScope.TenantId);

        _dbContext.Customers.Add(customer);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation("ix_customers_tenant_id_owner_subject_email"))
        {
            throw new InvalidOperationException("A customer with that email already exists", ex);
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

    private async Task EnsureEmailIsUniqueAsync(string email, OwnerScope ownerScope, CancellationToken cancellationToken)
    {
        var emailExists = await _dbContext.Customers
            .AsNoTracking()
            .AnyAsync(customer =>
                customer.Email.Value == email &&
                customer.OwnerSubject == ownerScope.OwnerSubject &&
                customer.TenantId == ownerScope.TenantId,
                cancellationToken);

        if (emailExists)
            throw new InvalidOperationException("A customer with that email already exists");
    }
}
