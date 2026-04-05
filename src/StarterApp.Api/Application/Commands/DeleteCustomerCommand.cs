namespace StarterApp.Api.Application.Commands;

public class DeleteCustomerCommand : ICommand, IRequest
{
    public int Id { get; set; }
}

public class DeleteCustomerCommandHandler : IRequestHandler<DeleteCustomerCommand>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheInvalidator _cacheInvalidator;

    public DeleteCustomerCommandHandler(ApplicationDbContext dbContext, ICacheInvalidator cacheInvalidator)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task HandleAsync(DeleteCustomerCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling DeleteCustomerCommand for Customer {CustomerId}", command.Id);

        Log.Information("Deleting customer {Id} with EF Core", command.Id);

        var customer = await _dbContext.Customers.FindAsync([command.Id], cancellationToken);
        if (customer == null)
        {
            Log.Warning("Customer {Id} not found for deletion", command.Id);
            throw new KeyNotFoundException($"Customer with ID {command.Id} not found");
        }

        var hasOrders = await _dbContext.Orders.AnyAsync(o => o.CustomerId == command.Id, cancellationToken);
        if (hasOrders)
        {
            Log.Warning("Customer {Id} cannot be deleted because they have existing orders", command.Id);
            throw new InvalidOperationException($"Cannot delete customer '{customer.Name}' because they have existing orders");
        }

        _dbContext.Customers.Remove(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _cacheInvalidator.InvalidateCustomerAsync(command.Id, cancellationToken);

        Log.Information("Deleted customer with ID: {CustomerId}", command.Id);
    }
}



