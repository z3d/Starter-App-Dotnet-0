namespace StarterApp.Domain.Entities;

public class Product
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    // Private setters to enforce immutability and encapsulation
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public int Stock { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    // Protected constructor for EF Core
    protected Product()
    {
        // Initialize default values to satisfy non-nullable warnings
        Name = string.Empty;
        Description = string.Empty;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    // Public constructor (replacing the factory method)
    public Product(string name, string? description, Money price, int stock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentOutOfRangeException.ThrowIfNegative(stock);

        ValidateName(name);
        ValidateDescription(description);

        Name = name;
        Description = description ?? string.Empty;
        Price = price;
        Stock = stock;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    // Domain methods
    public void UpdateDetails(string name, string? description, Money price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);

        ValidateName(name);
        ValidateDescription(description);

        Name = name;
        Description = description ?? string.Empty;
        Price = price;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    public void UpdateStock(int quantity)
    {
        if (Stock + quantity < 0)
            throw new InvalidOperationException("Cannot reduce stock below zero");

        Stock += quantity;
        LastUpdated = DateTimeOffset.UtcNow;
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

