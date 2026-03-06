# Architecture Review

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices across an e-commerce domain (Products, Customers, Orders) with Aspire orchestration and SQL Server.

**Score: 7.5/10**

---

## Strengths

### Clean Architecture with Convention-Enforced Boundaries

The project enforces architectural rules through **37 convention tests** across 5 test classes using Best.Conventional. This is the strongest feature of the codebase — architectural decisions are not just documented but mechanically verified on every test run.

| Test Class | Tests | What It Enforces |
|------------|-------|-----------------|
| `NamingConventionTests` | 9 | Endpoints, DTOs, commands, queries, handlers, validators, services, and test classes follow naming conventions |
| `CqrsConventionTests` | 6 | Command handlers don't touch `IDbConnection`; query handlers don't touch `DbContext`; every command/query has a handler; dual interface enforcement (`ICommand` + `IRequest<T>`) |
| `DomainConventionTests` | 8 | Private property setters on entities; immutable value objects; public getters on DTOs; non-public default constructors; `Equals`/`GetHashCode` overrides; async suffix; no async void; no `DateTime.Now` |
| `ApiConventionTests` | 8 | Endpoints don't access DB directly; validators are pure; DTOs have no instance methods; mappers are static; handlers don't dispatch to other handlers; domain doesn't reference API |
| `PersistenceConventionTests` | 6 | Every entity has a `DbSet`; value objects use `OwnsOne` not `DbSet`; enum properties configured; no static mutable state on `DbContext`; collection properties have private setters; migration scripts follow numbered prefix |

These tests catch entire categories of mistakes at compile time rather than in production.

### CQRS Implementation

The read/write split is cleanly executed:

- **Commands** flow through EF Core `DbContext` for writes, returning `*Dto` types
- **Queries** flow through Dapper `IDbConnection` for reads, returning `*ReadModel` types
- A **custom mediator** replaces MediatR, avoiding commercial licensing. It auto-discovers handlers via reflection and integrates validation as a pipeline behavior
- Convention tests mechanically prevent cross-contamination between read and write paths

### Rich Domain Models

Entities contain real behavior, not just properties:

- **`Order`** has a state machine (Pending > Confirmed > Processing > Shipped > Delivered, with cancellation from valid states). `IsValidStatusTransition()` uses a switch expression that makes valid/invalid transitions explicit. `Confirm()` requires non-empty items. `Cancel()` prevents cancellation of delivered orders.
- **Value objects** (`Money`, `Email`) are immutable with private constructors, static factory methods, and proper `Equals`/`GetHashCode` overrides
- **`OrderItem`** encapsulates GST calculation logic with multiple derived values (`GetUnitPriceIncludingGst`, `GetTotalPriceExcludingGst`, `GetTotalPriceIncludingGst`, `GetTotalGstAmount`)
- Private setters and protected constructors enforce encapsulation throughout
- `Reconstitute()` factory methods handle database hydration without bypassing creation-time invariants

### Property-Based Testing

FsCheck tests go beyond typical unit tests by generating hundreds of random inputs per test:

- Money arithmetic (commutativity, associativity, currency mismatch)
- Order state machine (valid/invalid transitions across random states)
- OrderItem GST calculations and boundary conditions
- Email format edge cases

### Custom Mediator

The mediator at `Infrastructure/Mediator/Mediator.cs` is well-designed:

- Auto-discovery and registration via `MediatorServiceExtensions.AddMediator()`
- Validation pipeline: validators run before handlers, collecting all errors before throwing
- Supports both `IRequest<TResponse>` (returns value) and `IRequest` (void) dispatch
- Keeps the codebase free from MediatR's commercial licensing constraints

### DevOps and Observability

- **Aspire orchestration** — `AppHost/Program.cs` wires up API, SQL Server, Seq, and DbMigrator with proper `WaitFor` dependencies and optional dev tunnel support
- **Serilog** structured logging with console, file, Seq, and OpenTelemetry sinks
- **OpenTelemetry** metrics (ASP.NET Core, HTTP, runtime) and tracing via `ServiceDefaults`
- **Docker** multi-stage build with docker-compose (API + SQL Server + Seq)
- **CI** pipeline with GitHub Actions (build + test on push/PR)
- **Health checks** at `/health` and `/alive`
- **Password masking** in log output — implemented consistently across `Program.cs`, `DatabaseMigrationEngine`, and `DbMigrator`

### Build Quality

`Directory.Build.props` enforces quality globally:

- Warnings as errors
- Nullable reference types
- Deterministic builds with `PathMap`
- Package lock files with `RestoreLockedMode` in CI
- Global Roslyn analyzers (`Microsoft.CodeAnalysis.Analyzers`)

### .NET 10 Features

Good adoption of modern .NET:

- Native OpenAPI support (`AddOpenApi` with document transformer)
- `StatusCodeSelector` on `UseExceptionHandler` for type-based exception-to-status mapping
- Minimal APIs with `MapGroup` and endpoint metadata
- Scalar API reference UI replacing Swagger
- `ArgumentOutOfRangeException.ThrowIfNegativeOrZero()` and similar guard clause helpers

---

## Weaknesses

### 1. Read Model Totals Are Never Written — CQRS Data Consistency Bug

**Severity: High**

The `Orders` table has `TotalExcludingGst`, `TotalIncludingGst`, `TotalGstAmount`, and `Currency` columns (defined in `0004_CreateOrdersTable.sql` with `DEFAULT 0.00`). However, the `Order` domain entity does not map these columns — it computes totals via methods (`GetTotalExcludingGst()`, etc.) from its `Items` collection.

**The write side never updates these columns.** When `CreateOrderCommand` saves an order via EF Core, the totals remain at 0.00. The `OrderMapper.ToDto()` correctly computes totals from the domain for command responses, but all **Dapper read queries** (`GetOrderByIdQuery`, `GetOrdersByCustomerQuery`, `GetOrdersByStatusQuery`) select these columns directly from the table — returning 0 for every order's totals.

```sql
-- GetOrderByIdQuery reads TotalExcludingGst directly from table (always 0)
SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst,
       TotalGstAmount, Currency, LastUpdated
FROM Orders WHERE Id = @Id
```

**Recommendation:** Either (a) compute totals in the Dapper queries via `JOIN`/subquery against `OrderItems`, or (b) update the totals columns in the command handlers after saving items. Option (a) is more consistent with CQRS principles since it avoids denormalization drift.

### 2. No Authentication or Authorization

**Severity: High**

No auth exists. Rate limiting and security headers are present, but they protect an open API. For a starter template, this is the most common thing teams need to add first.

**Recommendation:** Add a JWT bearer setup with a placeholder identity provider. Even a minimal `AddAuthentication().AddJwtBearer()` with `RequireAuthorization()` on the endpoint groups would demonstrate the pattern.

### 3. CreateOrderCommand Has Two SaveChanges Without Transaction Boundary

**Severity: Medium**

`CreateOrderCommandHandler` calls `SaveChangesAsync` twice: once to get the generated order ID, then again after creating items. If the second save fails, the database contains an order with no items — a partial write.

```csharp
_dbContext.Orders.Add(order);
await _dbContext.SaveChangesAsync(cancellationToken); // Gets ID

foreach (var itemCommand in command.Items) { /* ... */ }
await _dbContext.SaveChangesAsync(cancellationToken); // Could fail here
```

**Recommendation:** Wrap both saves in an explicit `using var transaction = await _dbContext.Database.BeginTransactionAsync()` with commit/rollback. Alternatively, use a temporary negative ID strategy or Hi-Lo sequence to avoid the two-phase save entirely.

### 4. Public `SetId()` Methods Break Domain Encapsulation

**Severity: Medium**

`Customer.SetId()`, `Product.SetId()`, and `OrderItem.SetId()` are public methods that allow any caller to reassign entity identity. This directly undermines the private setter discipline enforced by convention tests.

```csharp
// Anyone can call this
product.SetId(999);
```

**Recommendation:** Remove these methods entirely. EF Core sets `Id` via the private setter or backing field. If cross-assembly access is needed for testing, make the method `internal` and use `InternalsVisibleTo` for the test project.

### 5. UpdateOrderStatus and CancelOrder Use AsNoTracking Then Update

**Severity: Medium**

Both `UpdateOrderStatusCommandHandler` and `CancelOrderCommandHandler` load the order with `AsNoTracking()`, reconstitute it in memory, mutate it, then call `_dbContext.Orders.Update(order)`. The `Update()` method marks **all properties** as modified, which overwrites any concurrent changes to other fields.

Additionally, both handlers call `SaveChangesAsync()` without passing `cancellationToken`, inconsistent with other handlers.

**Recommendation:** Either track the entity normally (remove `AsNoTracking`) and let EF Core detect only changed properties, or use `ExecuteUpdateAsync` for targeted column updates. Pass `cancellationToken` to all `SaveChangesAsync` calls.

### 6. Thin Application Layer

**Severity: Medium**

Most command handlers are CRUD pass-through: receive DTO, create/update entity, save, return DTO. The mediator + validator pipeline is well-built infrastructure, but the handlers don't demonstrate complex business workflows.

`CreateOrderCommandHandler` is the closest to real business logic (validates customer exists, validates products exist), but it doesn't check stock availability or reserve inventory — a natural next step for an e-commerce domain.

**Recommendation:** Enhance `CreateOrderCommandHandler` to check product stock before creating items and decrement stock on creation. This demonstrates cross-aggregate coordination without adding excessive complexity.

### 7. Sparse Validation Coverage

**Severity: Medium**

Only 3 out of 9 commands have validators (`CreateOrderCommandValidator`, `UpdateOrderStatusCommandValidator`, `GetOrdersByStatusQueryValidator`). The remaining 6 commands rely on domain constructor exceptions for validation.

While domain validation exists, it throws `ArgumentException`/`ArgumentNullException`/`KeyNotFoundException` with single error messages. The validator pipeline returns structured `ValidationError` collections with property names — a much better API experience. Without validators, clients can't get multiple validation errors in one response.

**Recommendation:** Add validators for `CreateCustomerCommand` and `CreateProductCommand` at minimum, since these are the primary creation endpoints. Validate name length, email format, price range, and stock bounds before hitting the domain.

### 8. Database Migrations Run on API Startup

**Severity: Medium**

`DatabaseMigrator.MigrateDatabase()` runs in `Program.cs` on every API startup. The separate `DbMigrator` project exists and is wired into Aspire as a standalone service, but the API also runs migrations independently. With multiple API replicas, this creates a race condition.

**Recommendation:** Remove the migration call from `Program.cs` and rely exclusively on the `DbMigrator` service. If API-startup migration is needed for local development, gate it behind a configuration flag (`"RunMigrationsOnStartup": true`) that defaults to `false`.

### 9. Money.Subtract Can Produce Negative Amounts

**Severity: Low**

`Money.Create()` enforces `Amount >= 0`, but `Money.Subtract()` can produce negative amounts by subtracting a larger value from a smaller one. This bypasses the non-negative invariant:

```csharp
var small = Money.Create(5);
var large = Money.Create(10);
var negative = small.Subtract(large); // Amount = -5, valid Money instance
```

**Recommendation:** Add a guard in `Subtract` that throws if the result would be negative, or introduce a separate `MoneyDifference` type if negative amounts are intentionally valid for refunds/credits.

### 10. Missing Patterns for a Starter Template

**Severity: Low**

| Pattern | Impact |
|---------|--------|
| Domain events | No way to react to domain changes (e.g., "order created" > send email) |
| Outbox pattern | No reliable event publishing |
| Caching | No `IDistributedCache` or response caching |
| `PagedResult<T>` | Endpoints accept `page`/`pageSize` but return raw collections without total count |
| API versioning | Routes use `/api/v1/` prefix strings but no formal versioning library |

**Recommendation:** Domain events are the highest-value addition. The custom mediator already has the infrastructure to dispatch them.

---

## Minor Issues

- **Dockerfile installs SQL Server ODBC tools in production image** — adds ~200MB for debugging utilities that shouldn't ship. Move to a separate debug stage or remove.
- **CI pipeline skips integration tests** — `--filter "FullyQualifiedName!~Integration"` means Testcontainers-based tests never run in CI. Add a separate job with Docker support.
- **CORS is fully permissive in development** — `AllowAnyOrigin()` is standard for dev but worth a code comment explaining the production restriction.
- **`Email.IsValidEmail` uses try/catch for flow control** — `MailAddress` parsing with exception handling is functional but allocates on invalid input. Consider a regex pre-check.
- **No `appsettings.Development.json`** — running the API without Aspire requires manually setting connection strings. A development config with `localhost` defaults would improve standalone DX.
- **`Order.Reconstitute()` is public** — correct for cross-assembly database hydration, but could be misused to construct orders in arbitrary states. Consider making it `internal` with `InternalsVisibleTo`.
- **Scalar UI replaces Swagger UI** — may slow onboarding for developers expecting Swagger, though Scalar is a clear upgrade in functionality.

---

## Test Coverage Summary

| Category | Files | What's Tested |
|----------|-------|---------------|
| Domain unit tests | 6 | Entity creation, validation, state transitions, value object behavior |
| Property-based (FsCheck) | 5 | Money arithmetic invariants, order state machine, GST calculations, email validation |
| Convention tests | 5 (37 tests) | Architecture boundaries, naming, CQRS separation, domain encapsulation, persistence mapping |
| Application tests | 4 | Command handler behavior with mocked DbContext |
| Integration tests | 4+ | Full API endpoint testing with Testcontainers SQL Server, DbUp migrations, ProblemDetails responses |
| Test builders | 3 | Fluent builders for Customer, Product, Order |

**Gap:** Application-layer handler tests are sparse relative to the convention and domain tests. The most complex handler (`CreateOrderCommandHandler`) has a test, but the order status/cancellation handlers (which have the `AsNoTracking`/`Update` issue) lack targeted tests.

---

## Verdict

A well-engineered starter template that gets the hard things right: architecture enforcement through 37 convention tests, proper CQRS separation, rich domain modeling with state machines and value objects, and modern DevOps with Aspire orchestration.

The main issues are (1) a CQRS data consistency bug where order totals are never written to the read model columns, (2) no authentication, and (3) several domain encapsulation holes (`SetId()`, two-phase save, `AsNoTracking` + `Update`). These are all fixable without structural changes.

The convention tests remain the standout feature. They catch categories of architectural drift that code review alone would miss, and they scale as the codebase grows.

**Best suited for:** Teams starting a new .NET API who want architectural guardrails from day one and are willing to add auth, domain events, and fix the read model consistency gap themselves.