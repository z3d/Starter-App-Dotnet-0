# Domain-Driven Design Implementation

## Domain Entities

**Core Pattern**: Entities with private setters, public constructors, and domain behavior.

```csharp
public class Product
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public int Stock { get; private set; }

    protected Product() { } // EF Core constructor

    public Product(string name, string description, Money price, int stock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentOutOfRangeException.ThrowIfNegative(stock);

        Name = name;
        Description = description;
        Price = price;
        Stock = stock;
    }

    public void UpdateDetails(string name, Money price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        Name = name;
        Price = price;
    }
}
```

## Value Objects

**Core Pattern**: Immutable objects with static factory methods and proper equality.

```csharp
public class Money
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length > 3)
            throw new ArgumentException("Currency code cannot exceed 3 characters", nameof(currency));
        return new Money(amount, currency);
    }

    // Must override Equals(object) and GetHashCode() — convention tests enforce this
}
```

## Entity-Value Object Integration

**Use single entity with embedded value objects** (not dual representation):

```csharp
// CORRECT - Single entity with embedded value objects
public class Customer
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Email Email { get; private set; } = null!; // Embedded value object

    public void UpdateEmail(Email newEmail)
    {
        ArgumentNullException.ThrowIfNull(newEmail);
        Email = newEmail;
    }
}

// AVOID - Dual representation creates unnecessary complexity
// public class CustomerValue { }  // Don't create this
// public class CustomerEntity { } // When you already have Customer
```

## Reconstitute Pattern (Test-Only)

`internal static` factory method for rebuilding aggregates in arbitrary states. Visible to the test project via `InternalsVisibleTo`.

```csharp
internal static Order Reconstitute(int id, int customerId, DateTime orderDate,
    OrderStatus status, DateTime lastUpdated, List<OrderItem> items)
```

**Why it exists**: The public `Order(customerId)` constructor enforces creation-time invariants (status = Pending, empty items). Property-based fuzz tests need to create orders in arbitrary states (e.g., Shipped, Delivered) without going through the full state machine.

**Not for production handlers**: Command handlers load aggregates via EF Core with `.Include(o => o.Items)` on a tracked entity, mutate through domain methods, and call `SaveChangesAsync`. EF Core detects only the changed properties. Do not use `AsNoTracking` + `Reconstitute` + `Update` — that marks all columns modified and creates lost-update risks.
