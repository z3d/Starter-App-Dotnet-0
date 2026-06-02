# Modular Monolith — Candidate Approaches

> Status: **exploratory / not adopted.** This document records two ways the codebase
> *could* evolve from its current layered monolith toward a modular monolith, and the
> trade-offs that distinguish them. It is decision input, not a committed plan.

## Where we are today

This solution is a **layered (Clean Architecture) monolith**, not a modular monolith.
Code is partitioned by **technical concern**, not by **business capability**:

- `Domain/Entities/` holds `Customer`, `Order`, `OrderItem`, `Product` together
- `Application/Commands/` and `Application/Queries/` hold every aggregate's handlers in flat folders
- One `ApplicationDbContext`, one set of layers, shared freely across Customer / Catalog / Orders

Cross-domain work is done by reaching directly across aggregates. The clearest example is
`CreateOrderCommandHandler`, which in a single EF transaction:

1. reads `_dbContext.Customers` (Customers domain),
2. runs an atomic `UPDATE products SET stock = stock - qty WHERE stock >= qty` via
   `ExecuteUpdateAsync` (Catalog/Inventory domain) — the anti-oversell guard,
3. inserts `orders` + `order_items` (Orders domain),
4. all under one `BeginTransactionAsync` / `SaveChangesAsync`.

The runtime is already multi-process (API, Functions, DbMigrator orchestrated by Aspire,
communicating via the outbox + Service Bus), but the **code** has no module boundaries.

### Candidate modules

| Module | Owns (aggregates / tables) | Raises |
|---|---|---|
| **Customers** | `Customer` → `customers` | `customer.created/updated/deleted` |
| **Catalog** | `Product` → `products` (incl. stock) | `product.created/updated/deleted` |
| **Orders** | `Order`, `OrderItem` → `orders`, `order_items` | `order.created/cancelled`, status events |

Stock lives in Catalog because it is a `Product` property today. If inventory grows its own
lifecycle later, split it into a fourth module; until then, keeping it in Catalog avoids a
module that would only ever share Catalog's transaction.

---

## Approach A — Synchronous modules via published contracts

Modules talk through small, intention-revealing `Contracts/` interfaces resolved by DI.
Invariant-critical work stays synchronous and in-process; async events are reserved for
notification-only side effects.

### Physical structure

```
src/StarterApp.Api/
  Modules/
    Orders/
      Contracts/            <- PUBLIC. Other modules may reference ONLY this.
        IOrderReads.cs
        OrderPlacedEvent.cs
      Domain/               <- internal: Order, OrderItem
      Application/          <- internal: command/query handlers, validators
      Data/                 <- internal: OrderConfiguration, owns the "orders" schema
      OrdersModule.cs       <- DI registration (AddOrdersModule)
    Catalog/
      Contracts/            <- IInventory, ICatalogReads, stock events
      Domain/ Application/ Data/ ...
    Customers/
      Contracts/            <- ICustomerReads
      ...
  Shared/                   <- shared kernel (see below)
```

Start **folder-only** in the single API project; graduate to **one `.csproj` per module**
later so the C# `internal` keyword enforces boundaries for free. The convention tests below
give ~90% of the boundary protection without the project split.

### The contract surface

Keep it tiny. Two flavours: **reads** (for query composition) and **operations** (for
cross-module commands). `OwnerScope` flows through every contract so owner-only
authorization stays intact and is enforced inside each module against its own tables.

```csharp
// Catalog/Contracts — the one Orders actually needs
public interface IInventory
{
    // Atomic reserve; returns the catalog facts Orders must snapshot onto the order line.
    Task<StockReservation> ReserveStockAsync(
        int productId, int quantity, OwnerScope scope, CancellationToken ct);

    Task ReleaseStockAsync(int productId, int quantity, OwnerScope scope, CancellationToken ct);
}

public sealed record StockReservation(int ProductId, string ProductName, Money UnitPrice);

// Catalog/Contracts — for read composition
public interface ICatalogReads
{
    Task<IReadOnlyList<ProductSummary>> GetProductsAsync(
        IReadOnlyCollection<int> ids, OwnerScope scope, CancellationToken ct);
}

// Customers/Contracts
public interface ICustomerReads
{
    Task<CustomerSummary?> GetAsync(int customerId, OwnerScope scope, CancellationToken ct);
}
```

### How modules communicate

Two channels, chosen by **intent**:

**1. Synchronous — anything with an invariant at stake (commands, invariant-critical reads).**
`CreateOrderCommandHandler` no longer touches `_dbContext.Products`; it injects `IInventory`
and `ICustomerReads`:

```csharp
public CreateOrderCommandHandler(
    ApplicationDbContext db,    // Orders' own context/schema only
    IInventory inventory,       // Catalog contract
    ICustomerReads customers,   // Customers contract
    IOwnerOnlyPolicy ownerPolicy) { ... }

// inside the existing execution-strategy retry delegate, same transaction as today:
var customer = await customers.GetAsync(cmd.CustomerId, scope, ct)
               ?? throw new KeyNotFoundException(...);
foreach (var item in cmd.Items)
{
    var reserved = await inventory.ReserveStockAsync(item.ProductId, item.Quantity, scope, ct);
    order.AddItem(reserved.ProductId, reserved.ProductName, item.Quantity,
                  reserved.UnitPrice, OrderItem.DefaultGstRate);
}
db.Orders.Add(order);
await db.SaveChangesAsync(ct);   // still one SaveChanges, still one transaction
```

**The deliberate compromise:** the `IInventory` implementation enlists in Orders' ambient
transaction. Same process, same `ApplicationDbContext`-backed connection, so the atomic
`UPDATE ... WHERE stock >= qty` still runs inside Orders' unit of work and **overselling stays
impossible**. A strict modular monolith forbids sharing a transaction across modules; we
accept it *specifically* for the stock invariant, document why, and pin it with a convention
test. The retry/idempotency machinery (stable `orderId`, `ChangeTracker.Clear()`, execution
strategy) is unchanged.

**2. Asynchronous — notification-only, no invariant.** Use the existing outbox + Service Bus.
Do **not** route Orders→Catalog through events in Approach A; that would reintroduce the
eventual-consistency window we are trying to avoid. Async stays for genuine fire-and-forget:
the `email-notifications` subscription (already notification-only) and future analytics-style
consumers. Each module publishes its own `Contracts/` events identified by their
`const Contract` string, so renaming a C# class never breaks routing.

### Queries

- **Intra-module queries are unchanged.** `GetOrderByIdQuery` only reads `orders` +
  `order_items` — both owned by Orders. Its raw Dapper SQL stays as-is.
- **Cross-module reads** must not JOIN another module's tables (that re-couples schemas).
  Two ways out, in order of preference:
  1. **Denormalization — already in use.** `order_items` snapshots `product_name` and
     `unit_price_excluding_gst` at order time rather than FK-joining `products`. Each module
     keeps its own copy of the facts it needs, so most cross-module reads disappear.
  2. **Read-side composition.** Query own rows via Dapper, then call `ICatalogReads` /
     `ICustomerReads` and stitch the DTO in memory — no cross-schema JOIN.
  - Genuine reporting/analytics that must span modules goes to a read-only reporting
    schema/views with accepted eventual consistency.

### Data ownership

- Each module owns its tables; **no FK or JOIN crosses a module boundary.**
  `orders.customer_id` and `order_items.product_id` become *logical* references (plain `int`,
  no DB FK), validated through contracts at write time instead of by referential integrity.
- Optionally one DB schema per module (`orders.orders`, `catalog.products`) to make the
  boundary visible and grantable. DbUp handles this; constraint-naming conventions extend to
  schema-qualified names.

### Enforcement (mechanical — matches this repo's philosophy)

Add convention tests so the boundary cannot drift:

1. A handler in `Modules/Orders/**` may reference other modules only via `Modules/*/Contracts`
   — never their `Domain`/`Data` internals.
2. No cross-module `DbContext` navigation (e.g. Orders handlers may not touch the
   `Products`/`Customers` `DbSet`s). *This is the test that would have caught today's
   `_dbContext.Products` access.*
3. No cross-module DB foreign key (scan migrations for `fk_*` spanning two module schemas).
4. `Contracts/` namespaces contain only POCOs/interfaces — no EF entities or handlers.

When the project split happens, rules 1–2 also become compile errors via `internal`.

---

## Approach B — Asynchronous saga via the outbox

The textbook modular-monolith answer for cross-module commands. Modules never share a
transaction; cross-module work is choreographed through domain events.

Order creation becomes a saga:

1. Orders creates the order in a `Pending` state and raises `OrderPlaced`.
2. Catalog/Inventory subscribes, attempts the reservation, and raises `StockReserved` or
   `StockReservationFailed`.
3. Orders reacts: confirm the order, or compensate (cancel + release).

The infrastructure already exists end to end: outbox capture inside `SaveChanges`, the
Service Bus `domain-events` topic, the `inventory-reservation` subscription, and
`CancelOrderCommand` / `OrderCancellationService` for compensation.

**Cost:** prevention is traded for **detection + compensation**. Overselling becomes possible
within the reservation window and is undone after the fact; the order is briefly visible as
`Pending`; and a saga/process-manager state machine plus compensation paths must be built and
tested.

---

## Recommendation

Adopt **Approach A** as the structural target, with the single conscious exception that
`IInventory` shares Orders' transaction to preserve the existing atomic anti-oversell
guarantee. Reserve **Approach B**'s async saga for steps that genuinely cross a
bounded-context line *and* tolerate eventual consistency — e.g. email notification, which is
already async and correctly notification-only.

Rationale: the current design chose strong consistency for stock because overselling is
user-visible and expensive to compensate. Approach A keeps that guarantee while still
delivering module isolation (code + schema + enforced contracts). Approach B is "more purely
modular" but pays for it with an eventual-consistency window and saga machinery that this
invariant does not warrant.
