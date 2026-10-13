# EF Mapping, Migrations, and Aspire Wiring

## Mapping shapes

Value-object embedding with `OwnsOne` and explicit column names:

```csharp
modelBuilder.Entity<Product>()
    .OwnsOne(p => p.Price, priceBuilder => {
        priceBuilder.Property(m => m.Amount).HasColumnName("price_amount");
        priceBuilder.Property(m => m.Currency).HasColumnName("price_currency");
    });

modelBuilder.Entity<Customer>()
    .OwnsOne(c => c.Email, emailBuilder => {
        emailBuilder.Property(e => e.Value).HasColumnName("email");
    });
```

Aggregate navigation with backing-field access — the root owns the collection privately, EF populates it via `.Include()`, and items added through `Order.AddItem()` get their FK set on save:

```csharp
modelBuilder.Entity<Order>(orderBuilder => {
    orderBuilder.HasMany(o => o.Items)
        .WithOne()
        .HasForeignKey(oi => oi.OrderId)
        .OnDelete(DeleteBehavior.Cascade);
    orderBuilder.Navigation(o => o.Items)
        .UsePropertyAccessMode(PropertyAccessMode.Field);
});
```

Optimistic concurrency via the PostgreSQL `xmin` system column:

```csharp
builder.Property(o => o.RowVersion)
    .HasColumnName("xmin")
    .IsRowVersion();
```

`Order` and `Product` carry this because their stale writes corrupt state or inventory. The API maps the resulting `DbUpdateConcurrencyException` to 409.

## Constraint naming

Every constraint gets an explicit name, enforced by convention test from the first migration onward. Anonymous constraints get system-generated names that later migrations can only discover with fragile dynamic SQL.

| Prefix | Type | Example |
|--------|------|---------|
| `pk_` | Primary key | `CONSTRAINT pk_orders PRIMARY KEY (id)` |
| `fk_` | Foreign key | `CONSTRAINT fk_orders_customer_id FOREIGN KEY (customer_id) REFERENCES customers(id)` |
| `df_` | Default | `CONSTRAINT df_orders_status DEFAULT 'Pending'` |
| `ck_` | Check | `CONSTRAINT ck_products_stock_non_negative CHECK (stock >= 0)` |
| `ix_` | Index | `CREATE INDEX ix_orders_order_date ON orders (order_date DESC)` |

In `ALTER TABLE`, use the `ADD CONSTRAINT` form:

```sql
ALTER TABLE orders ADD CONSTRAINT ck_orders_status
    CHECK (status IN ('Pending', 'Confirmed', 'Processing', 'Shipped', 'Delivered', 'Cancelled'));
```

The default (`df_`) case is the one that bites: an unnamed `DEFAULT` looks harmless in the creating migration and is undroppable-by-name in the one that changes it.

## Migration execution per environment

Migrations run exclusively through the DbMigrator console app — never in API startup, which races across replicas.

- **Aspire:** AppHost runs DbMigrator with `WaitFor` on PostgreSQL.
- **Container deployments:** run the DbMigrator image/job to completion before starting API replicas.
- **Standalone dev:** `dotnet run --project src/StarterApp.DbMigrator`.
- **Integration tests:** `TestFixture.RunDbUpMigrations()`.

Scripts are embedded resources in the DbMigrator project with sequential names (`0001_CreateTables.sql`, `0002_AddIndexes.sql`). A convention test verifies every `Scripts/*.sql` file on disk is embedded in the built assembly — an unembedded script silently never runs.

The migrator runs before the new build starts, so the previous build serves against the new schema until the rollout ends. From script 0010 a migration is therefore one phase of an expand/contract change, and its name says which (`MigrationCompatibilityConventionTests`):

- `NNNN_Expand_Name.sql` adds. It may not `DROP` a table, column, view or schema, `RENAME`, change a column's type, `SET NOT NULL`, `TRUNCATE`, `DELETE`, add a `NOT NULL` column without a `DEFAULT`, or add a `UNIQUE`, `CHECK` or `PRIMARY KEY` constraint or unique index to a table that already exists. Backfills (`UPDATE`), nullable columns, defaulted columns, new tables and plain indexes are fine.
- `NNNN_Contract_Name.sql` removes what an earlier Expand made redundant, in a later release, and names it in a header: `-- completes: 0010_Expand_Name.sql`. The test checks the named script exists with a lower number; that it shipped in an earlier release is the deploy pipeline's rule, not the test's.

A rename is two releases: Expand adds the new column and the code writes both; Contract drops the old one once no deployed build reads it. Scripts 0001 to 0009 keep their names because renaming an embedded script re-runs it under DbUp.

## Connection-string resolution

Two resolvers, deliberately different:

- **API** (`src/StarterApp.Api/Program.cs`): `GetConnectionString("database") ?? throw ...` — only `database`, no fallback. Aspire injects it; a misconfigured deployment fails loudly rather than quietly using the wrong database.
- **DbMigrator** (`src/StarterApp.DbMigrator/Program.cs`): `database` → `postgres` → `DefaultConnection`, the last being the local `appsettings.json` fallback for standalone migration runs.

## Aspire wiring

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

Orchestration-owned infrastructure (PostgreSQL, Redis, Service Bus, Blob storage) is wired in AppHost; Aspire is the only supported local orchestration path. `ServiceDefaults` carries the shared cross-cutting setup: OpenTelemetry instrumentation, service discovery, resilience, health checks.
