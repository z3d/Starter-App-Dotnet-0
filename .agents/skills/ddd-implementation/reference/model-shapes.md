# Domain Model Shapes

## Entity

Private setters, a protected EF constructor, a validating public constructor with ownership stamping, and behaviour methods for mutation:

```csharp
public class Product
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public string OwnerSubject { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public int Stock { get; private set; }

    protected Product() { } // EF Core constructor

    public Product(string name, string? description, Money price, int stock, string ownerSubject, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentOutOfRangeException.ThrowIfNegative(stock);
        OwnershipDefaults.Validate(ownerSubject, tenantId);

        Name = name;
        Description = description ?? string.Empty;
        Price = price;
        Stock = stock;
        OwnerSubject = ownerSubject;
        TenantId = tenantId;
    }

    public void UpdateDetails(string name, string? description, Money price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        Name = name;
        Description = description ?? string.Empty;
        Price = price;
    }
}
```

Every owner-scoped aggregate (`Product`, `Customer`, `Order`) follows this ownership pattern — the constructor takes `ownerSubject`/`tenantId` and calls `OwnershipDefaults.Validate(...)`.

## Value object

Immutable, private constructor, static factory that validates **and normalizes**:

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

    public static Money Create(decimal amount, string currency = "USD")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, MaxAmount);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        // Currency must be EXACTLY three ISO letters — not merely "at most 3 chars".
        if (!IsValidCurrencyCode(currency))
            throw new ArgumentException("Currency code must be a three-letter ISO code", nameof(currency));

        // Normalize: round to 2 dp (whole minor units) and upper-case the currency.
        var rounded = decimal.Round(amount, CurrencyDecimalPlaces, MidpointRounding.AwayFromZero);
        return new Money(rounded, currency.ToUpperInvariant());
    }

    // Overrides Equals(object) and GetHashCode(), implements IEquatable<Money>
    // (convention-enforced; IEquatable avoids boxing in LINQ).
}
```

Value objects are embedded in the entity that owns them (`Customer.Email`, `Product.Price`) — mutation goes through an entity method (`UpdateEmail(Email newEmail)`), never a setter. Don't create a parallel `CustomerValue`/`CustomerEntity` pair; dual representation is prohibited.

## Reconstitute

```csharp
internal static Order Reconstitute(Guid id, int customerId, DateTimeOffset orderDate,
    OrderStatus status, DateTimeOffset lastUpdated, List<OrderItem> items)
```

`internal static`, visible to the test project via `InternalsVisibleTo`.

**Why it exists:** the public `Order(...)` constructor and `RecordCreation()` enforce creation-time invariants — the constructor sets status = Pending, `RecordCreation()` guards against an empty order. Property-based fuzz tests need orders in arbitrary states (Shipped, Delivered) without walking the full state machine, and Reconstitute is that escape hatch.

**Why it is not a production path:** command handlers load aggregates via EF with `.Include(o => o.Items)` on a tracked entity, mutate through domain methods, and call `SaveChangesAsync` — EF then detects only the changed properties. `AsNoTracking` + `Reconstitute` + `Update` marks *all* columns modified, which both bloats the update and silently overwrites concurrent writes.

`Order.Id` is a `Guid` (client-assigned `Guid.CreateVersion7()` — required because `Order` raises a creation event), and all timestamps are `DateTimeOffset` (`DomainConventionTests.DomainTypes_MustUseDateTimeOffsetNotDateTime`).
