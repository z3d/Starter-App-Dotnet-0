using StarterApp.Api.Data;

namespace StarterApp.Api.Application.Commands;

public class CustomerCommandService : ICustomerCommandService
{
    private readonly ApplicationDbContext _dbContext;

    public CustomerCommandService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Customer> CreateCustomerAsync(string name, Email email)
    {
        Log.Information("Creating customer {Name} with EF Core", name);
        
        var customer = new Customer(name, email);
        
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();
        
        return customer;
    }

    public async Task<Customer?> UpdateCustomerAsync(int id, string name, Email email)
    {
        Log.Information("Updating customer {Id} with EF Core", id);
        
        var customer = await _dbContext.Customers.FindAsync(id);
        if (customer == null)
        {
            Log.Warning("Customer {Id} not found for update", id);
            return null;
        }

        customer.UpdateDetails(name, email);
        
        await _dbContext.SaveChangesAsync();
        return customer;
    }

    public async Task<bool> DeleteCustomerAsync(int id)
    {
        Log.Information("Deleting customer {Id} with EF Core", id);
        
        var customer = await _dbContext.Customers.FindAsync(id);
        if (customer == null)
        {
            Log.Warning("Customer {Id} not found for deletion", id);
            return false;
        }
        
        _dbContext.Customers.Remove(customer);
        await _dbContext.SaveChangesAsync();
        return true;
    }
}