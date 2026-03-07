namespace StarterApp.Domain.Entities;

public class Product
{
    // Private setters to enforce immutability and encapsulation
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public int Stock { get; private set; }
    public DateTime LastUpdated { get; private set; }

    // Protected constructor for EF Core
    protected Product()
    {
        // Initialize default values to satisfy non-nullable warnings
        Name = string.Empty;
        Description = string.Empty;
        LastUpdated = DateTime.UtcNow;
    }

    // Public constructor (replacing the factory method)
    public Product(string name, string description, Money price, int stock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ArgumentNullException.ThrowIfNull(price);

        ArgumentOutOfRangeException.ThrowIfNegative(stock);

        Name = name;
        Description = description ?? string.Empty;
        Price = price;
        Stock = stock;
        LastUpdated = DateTime.UtcNow;
    }

    // Domain methods
    public void UpdateDetails(string name, string description, Money price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);

        Name = name;
        Description = description ?? string.Empty;
        Price = price;
        LastUpdated = DateTime.UtcNow;
    }

    public void UpdateStock(int quantity)
    {
        if (Stock + quantity < 0)
            throw new InvalidOperationException("Cannot reduce stock below zero");

        Stock += quantity;
        LastUpdated = DateTime.UtcNow;
    }

}



