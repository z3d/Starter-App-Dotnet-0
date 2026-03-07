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
        priceBuilder.Property(m => m.Amount).HasColumnName("PriceAmount");
        priceBuilder.Property(m => m.Currency).HasColumnName("PriceCurrency");
    });

// Customer configuration
modelBuilder.Entity<Customer>()
    .OwnsOne(c => c.Email, emailBuilder => {
        emailBuilder.Property(e => e.Value).HasColumnName("Email");
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

## Database Migrations

**DbUp with Embedded Scripts**:
- SQL scripts embedded in `DbMigrator` project
- Sequential naming: `0001_CreateTables.sql`, `0002_AddIndexes.sql`
- Automatic execution on startup with error handling
- Separate migration service for clean separation

## Connection String Resolution

Priority order: `database` → `DockerLearning` → `sqlserver` → `DefaultConnection`

## Aspire Configuration

### Aspire 13.1.2 Features

- **CLI Tools**: `aspire update` for automatic package updates
- **Dashboard**: GenAI visualizer for LLM telemetry, multi-resource console logs
- **Integrations**: OpenAI hosting, GitHub Models, Azure AI Foundry, Dev Tunnels
- **Deployment**: Azure Container App Jobs, built-in Azure deployment via CLI

### AppHost Project Setup

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);
var database = sql.AddDatabase("database");

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