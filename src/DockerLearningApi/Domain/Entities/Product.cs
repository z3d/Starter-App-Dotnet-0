using DockerLearningApi.Domain.ValueObjects;

namespace DockerLearningApi.Domain.Entities;

public class Product
{
    // Private setters to enforce immutability and encapsulation
    public int Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public Money Price { get; private set; }
    public int Stock { get; private set; }
    public DateTime LastUpdated { get; private set; }

    // Private constructor to enforce factory method pattern
    private Product() { }

    // Factory method
    public static Product Create(string name, string description, Money price, int stock)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name cannot be empty", nameof(name));

        if (price == null)
            throw new ArgumentNullException(nameof(price));

        if (stock < 0)
            throw new ArgumentException("Stock cannot be negative", nameof(stock));

        return new Product
        {
            Name = name,
            Description = description ?? string.Empty,
            Price = price,
            Stock = stock,
            LastUpdated = DateTime.UtcNow
        };
    }

    // Domain methods
    public void UpdateDetails(string name, string description, Money price)
    {
        if (!string.IsNullOrWhiteSpace(name))
            Name = name;

        if (description != null)
            Description = description;

        if (price != null)
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

    // For EF Core
    internal void SetId(int id)
    {
        Id = id;
    }
}