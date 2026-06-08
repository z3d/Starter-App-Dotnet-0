namespace StarterApp.Tests.TestBuilders;

public class CreateCustomerCommandBuilder
{
    private string _name = "Test Customer";
    private string _email = "test@example.com";

    public static CreateCustomerCommandBuilder Default() => new();

    public CreateCustomerCommandBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public CreateCustomerCommandBuilder WithEmail(string email)
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



