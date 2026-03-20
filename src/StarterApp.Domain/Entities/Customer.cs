namespace StarterApp.Domain.Entities;

public class Customer
{
    public const int MaxNameLength = 100;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Email Email { get; private set; } = null!;
    public DateTime DateCreated { get; private set; }
    public bool IsActive { get; private set; }

    protected Customer()
    {
        Name = string.Empty;
        DateCreated = DateTime.UtcNow;
        IsActive = true;
    }

    public Customer(string name, Email email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(email);

        ValidateName(name);

        Name = name;
        Email = email;
        DateCreated = DateTime.UtcNow;
        IsActive = true;
    }

    public void UpdateDetails(string name, Email email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(email);

        ValidateName(name);

        Name = name;
        Email = email;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static void ValidateName(string name)
    {
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Customer name cannot exceed {MaxNameLength} characters", nameof(name));
    }
}


