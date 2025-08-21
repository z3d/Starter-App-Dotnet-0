using StarterApp.Api.Data;

namespace StarterApp.Api.Application.Commands;

public class DeleteCustomerCommand : ICommand, IRequest
{
    public int Id { get; set; }
}

public class DeleteCustomerCommandHandler : IRequestHandler<DeleteCustomerCommand>
{
    private readonly ApplicationDbContext _dbContext;

    public DeleteCustomerCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        _dbContext.Customers.Remove(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("Deleted customer with ID: {CustomerId}", command.Id);
    }
}



