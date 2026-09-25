namespace StarterApp.Domain.Entities;

public class Product
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public string OwnerSubject { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public int Stock { get; private set; }
    public DateTimeOffset DateCreated { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; }
    public uint RowVersion { get; private set; }

    protected Product()
    {
        Name = string.Empty;
        Description = string.Empty;
    }

    public Product(string name, string? description, Money price, int stock, string ownerSubject, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentOutOfRangeException.ThrowIfNegative(stock);

        ValidateName(name);
        ValidateDescription(description);
        OwnershipDefaults.Validate(ownerSubject, tenantId);

        Name = name;
        Description = description ?? string.Empty;
        Price = price;
        OwnerSubject = ownerSubject;
        TenantId = tenantId;
        Stock = stock;
    }

    public void UpdateDetails(string name, string? description, Money price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);

        ValidateName(name);
        ValidateDescription(description);

        Name = name;
        Description = description ?? string.Empty;
        Price = price;
    }

    public void UpdateStock(int quantity)
    {
        if (Stock + quantity < 0)
            throw new DomainRuleException("Cannot reduce stock below zero");

        Stock += quantity;
    }

    private static void ValidateName(string name)
    {
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Product name cannot exceed {MaxNameLength} characters", nameof(name));
    }

    private static void ValidateDescription(string? description)
    {
        if (description?.Length > MaxDescriptionLength)
            throw new ArgumentException($"Product description cannot exceed {MaxDescriptionLength} characters", nameof(description));
    }

}
