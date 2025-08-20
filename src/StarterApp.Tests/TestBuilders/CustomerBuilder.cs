using StarterApp.Api.Application.Commands;

namespace StarterApp.Tests.TestBuilders;

public class CustomerBuilder
{
    private string _name = "Test Customer";
    private string _email = "test@example.com";

    public static CustomerBuilder Default() => new();

    public CustomerBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public CustomerBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public CreateCustomerCommand Build()
    {
        return new CreateCustomerCommand
        {
            Name = _name,
            Email = _email
        };
    }

    public static CreateCustomerCommand SimpleCustomer() => Default().Build();
}