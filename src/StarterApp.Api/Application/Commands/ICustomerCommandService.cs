namespace StarterApp.Api.Application.Commands;

public interface ICustomerCommandService
{
    Task<Customer> CreateCustomerAsync(string name, Email email);
    Task<Customer?> UpdateCustomerAsync(int id, string name, Email email);
    Task<bool> DeleteCustomerAsync(int id);
}
