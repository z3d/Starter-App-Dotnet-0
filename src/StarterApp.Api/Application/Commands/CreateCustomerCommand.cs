namespace StarterApp.Api.Application.Commands;

public class CreateCustomerCommand : ICommand, IRequest<CustomerDto>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public CreateCustomerCommandHandler(ApplicationDbContext dbContext, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _dbContext = dbContext;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<CustomerDto> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateCustomerCommand to return CustomerDto");

        Log.Information("Creating customer with EF Core");

        var email = Email.Create(command.Email);
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();
        await EnsureEmailIsUniqueAsync(email.Value, ownerScope, cancellationToken);

        // The database may commit the insert even if we never receive confirmation.
        // On retry, return an existing customer only if owner scope, email, and name match.
        // On the first attempt, let the unique constraint reject a concurrent duplicate
        // with 409; an existing customer must not count as a successful create.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        Customer? savedCustomer = null;
        var attempt = 0;

        await strategy.ExecuteAsync(cancellationToken, async ct =>
        {
            attempt++;

            // Clear tracker so a prior failed attempt's tracked entity does not leak into this
            // retry — otherwise two Added customers would be inserted on a second pass.
            _dbContext.ChangeTracker.Clear();

            if (attempt > 1)
            {
                var committedCustomer = await _dbContext.Customers
                    .FirstOrDefaultAsync(c =>
                        c.Email.Value == email.Value &&
                        c.OwnerSubject == ownerScope.OwnerSubject &&
                        c.TenantId == ownerScope.TenantId,
                        ct);
                if (committedCustomer != null)
                {
                    if (!string.Equals(committedCustomer.Name, command.Name, StringComparison.Ordinal))
                        throw new DomainRuleException("A customer with that email already exists");

                    savedCustomer = committedCustomer;
                    return;
                }
            }

            var customer = new Customer(command.Name, email, ownerScope.OwnerSubject, ownerScope.TenantId);
            _dbContext.Customers.Add(customer);
            try
            {
                await _dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation("ix_customers_tenant_id_owner_subject_email"))
            {
                throw new DomainRuleException("A customer with that email already exists", ex);
            }

            savedCustomer = customer;
        });

        Log.Information("Created new customer with ID: {CustomerId}", savedCustomer!.Id);

        // Map to DTO and return
        return new CustomerDto
        {
            Id = savedCustomer.Id,
            Name = savedCustomer.Name,
            Email = savedCustomer.Email.Value,
            DateCreated = savedCustomer.DateCreated,
            IsActive = savedCustomer.IsActive
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
            throw new DomainRuleException("A customer with that email already exists");
    }
}
