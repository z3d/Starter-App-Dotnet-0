---
name: data-access
description: EF Core configuration, value object embedding, DbUp migrations, Aspire setup. Use when modifying database schema, migrations, or data access code.
user-invocable: false
---

# Data Access & Configuration

## EF Core Configuration

**Value Object Embedding** with `OwnsOne`:

```csharp
// Product configuration
modelBuilder.Entity<Product>()
    .OwnsOne(p => p.Price, priceBuilder => {
        priceBuilder.Property(m => m.Amount).HasColumnName("price_amount");
        priceBuilder.Property(m => m.Currency).HasColumnName("price_currency");
    });

// Customer configuration
modelBuilder.Entity<Customer>()
    .OwnsOne(c => c.Email, emailBuilder => {
        emailBuilder.Property(e => e.Value).HasColumnName("email");
    });
```

**Aggregate Navigation Properties** with backing field access:

```csharp
// Order → OrderItems: EF Core manages FK, backing field stores collection
modelBuilder.Entity<Order>(orderBuilder => {
    orderBuilder.HasMany(o => o.Items)
        .WithOne()
        .HasForeignKey(oi => oi.OrderId)
        .OnDelete(DeleteBehavior.Cascade);
    orderBuilder.Navigation(o => o.Items)
        .UsePropertyAccessMode(PropertyAccessMode.Field);
});
```

This lets the aggregate root own the collection privately (`_items` backing field) while EF Core populates it via `.Include()`. Items added through `Order.AddItem()` get their `OrderId` set by EF on save — no need to know the ID upfront.

**Optimistic Concurrency**:

Use PostgreSQL `xmin` concurrency tokens for entities whose stale writes can corrupt state or inventory. `Order` and `Product` expose a `uint RowVersion` property with a private setter and their EF configurations map it to the `xmin` system column with `.IsRowVersion()`. `DbUpdateConcurrencyException` maps to `409 Conflict` at the API boundary.

```csharp
builder.Property(o => o.RowVersion)
    .HasColumnName("xmin")
    .IsRowVersion();
```

## Database Migrations

**DbUp with Embedded Scripts**:
- SQL scripts embedded in `DbMigrator` project
- Sequential naming: `0001_CreateTables.sql`, `0002_AddIndexes.sql`
- Automatic execution on startup with error handling
- Separate migration service for clean separation

**Constraint Naming Convention** (enforced by convention test from the first PostgreSQL migration onward):

Every constraint must be explicitly named. Anonymous/system-generated names require dynamic SQL to discover and drop, which is fragile and non-deterministic.

| Prefix | Type | Example |
|--------|------|---------|
| `pk_` | Primary Key | `CONSTRAINT pk_orders PRIMARY KEY (id)` |
| `fk_` | Foreign Key | `CONSTRAINT fk_orders_customer_id FOREIGN KEY (customer_id) REFERENCES customers(id)` |
| `df_` | Default | `CONSTRAINT df_orders_status DEFAULT 'Pending'` |
| `ck_` | Check | `CONSTRAINT ck_products_stock_non_negative CHECK (stock >= 0)` |
| `ix_` | Index | `CREATE INDEX ix_orders_order_date ON orders (order_date DESC)` |

In `ALTER TABLE ... ADD` statements, use the `ADD CONSTRAINT` form:
```sql
ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN ('Pending', 'Confirmed', 'Processing', 'Shipped', 'Delivered', 'Cancelled'));
```

## Connection String Resolution

Two resolvers, each in its own `Program.cs`:

- **API** (`src/StarterApp.Api/Program.cs`) resolves **only** `database` and throws if it is absent (`GetConnectionString("database") ?? throw new InvalidOperationException(...)`) — it relies on Aspire injecting the connection string, so there is no local fallback.
- **DbMigrator** (`src/StarterApp.DbMigrator/Program.cs`) resolves a fallback chain `database` → `postgres` → `DefaultConnection` (`databaseConnection ?? postgresConnection ?? defaultConnection`), with `DefaultConnection` serving as the local `appsettings.json` fallback for standalone migration runs.

## Aspire Configuration

### Aspire 13.2.3 Features

- **CLI Tools**: `aspire update` for automatic package updates
- **Dashboard**: GenAI visualizer for LLM telemetry, multi-resource console logs
- **Integrations**: OpenAI hosting, GitHub Models, Azure AI Foundry, Dev Tunnels
- **Deployment**: Azure Container App Jobs, built-in Azure deployment via CLI

### AppHost Project Setup

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
                      .WithLifetime(ContainerLifetime.Persistent);
var database = postgres.AddDatabase("database");

var api = builder.AddProject<Projects.StarterApp_Api>("api")
                 .WithReference(database)
                 .WaitFor(database);

var migrator = builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
                      .WithReference(database)
                      .WaitFor(database);

builder.Build().Run();
```

### ServiceDefaults Configuration

Shared cross-cutting concerns:
- OpenTelemetry instrumentation (ASP.NET Core, HTTP, Runtime)
- Service discovery and load balancing
- Resilience patterns with circuit breakers
- Health check endpoints
- Common middleware registration
