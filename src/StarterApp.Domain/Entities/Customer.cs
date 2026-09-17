namespace StarterApp.Domain.Entities;

public class Customer
{
    public const int MaxNameLength = 100;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Email Email { get; private set; } = null!;
    public string OwnerSubject { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public DateTimeOffset DateCreated { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; }
    public bool IsActive { get; private set; }

    protected Customer()
    {
        Name = string.Empty;
        DateCreated = DateTimeOffset.UtcNow;
        LastUpdated = DateCreated;
        IsActive = true;
    }

    public Customer(string name, Email email, string ownerSubject, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(email);

        ValidateName(name);
        OwnershipDefaults.Validate(ownerSubject, tenantId);

        Name = name;
        Email = email;
        OwnerSubject = ownerSubject;
        TenantId = tenantId;
        DateCreated = DateTimeOffset.UtcNow;
        LastUpdated = DateCreated;
        IsActive = true;
    }

    public void UpdateDetails(string name, Email email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(email);

        ValidateName(name);

        Name = name;
        Email = email;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    private static void ValidateName(string name)
    {
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Customer name cannot exceed {MaxNameLength} characters", nameof(name));
    }
}

