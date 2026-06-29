---
name: ddd-implementation
description: Domain entity and value object patterns — private setters, factory methods, Reconstitute. Use when creating or modifying domain models.
user-invocable: false
---

# Domain-Driven Design Implementation

## Domain Entities

**Core Pattern**: Entities with private setters, public constructors, and domain behavior.

Owner-scoped aggregates (`Product`, `Customer`, `Order`) must take `string ownerSubject, string tenantId` on their public constructor and validate them via `OwnershipDefaults.Validate(...)` — `DomainConventionTests.OwnerScopedAggregates_MustNotExposeOwnerlessPublicConstructors` fails the build for any ownerless public ctor.

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

(The constructor is elided above to focus on value-object embedding; the real `Customer` takes `string ownerSubject, string tenantId` and calls `OwnershipDefaults.Validate(...)` like every owner-scoped aggregate.)

## Reconstitute Pattern (Test-Only)

`internal static` factory method for rebuilding aggregates in arbitrary states. Visible to the test project via `InternalsVisibleTo`.

```csharp
internal static Order Reconstitute(Guid id, int customerId, DateTimeOffset orderDate,
    OrderStatus status, DateTimeOffset lastUpdated, List<OrderItem> items)
```

`Order.Id` is a `Guid` (client-assigned `Guid.CreateVersion7()`), and timestamps are `DateTimeOffset` — `DomainConventionTests.DomainTypes_MustUseDateTimeOffsetNotDateTime` forbids `DateTime` on domain types.

**Why it exists**: The public `Order(int customerId, string ownerSubject, string tenantId)` constructor and `RecordCreation()` enforce creation-time invariants (the ctor sets status = Pending; `RecordCreation()` guards against an empty order). Property-based fuzz tests need to create orders in arbitrary states (e.g., Shipped, Delivered) without going through the full state machine.

**Not for production handlers**: Command handlers load aggregates via EF Core with `.Include(o => o.Items)` on a tracked entity, mutate through domain methods, and call `SaveChangesAsync`. EF Core detects only the changed properties. Do not use `AsNoTracking` + `Reconstitute` + `Update` — that marks all columns modified and creates lost-update risks.