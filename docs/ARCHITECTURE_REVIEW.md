# Architecture Review

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices across an e-commerce domain (Products, Customers, Orders) with Aspire orchestration and SQL Server.

**Score: 7.5/10**

---

## Strengths

### Clean Architecture with Convention-Enforced Boundaries

The project enforces architectural rules through convention tests across 6 test classes using Best.Conventional. This is the strongest feature of the codebase — architectural decisions are not just documented but mechanically verified on every test run.

| Test Class | What It Enforces |
|------------|-----------------|
| `NamingConventionTests` | Endpoints, DTOs, commands, queries, handlers, validators, services, and test classes follow naming conventions |
| `CqrsConventionTests` | Command handlers don't touch `IDbConnection`; query handlers don't touch `DbContext`; every command/query has a handler; dual interface enforcement (`ICommand` + `IRequest<T>`) |
| `DomainConventionTests` | Private property setters on entities; immutable value objects; public getters on DTOs; non-public default constructors; `Equals`/`GetHashCode` overrides; async suffix; no async void; no `DateTime.Now` |
| `ApiConventionTests` | Endpoints don't access DB directly; validators are pure; DTOs have no instance methods; mappers are static; handlers don't dispatch to other handlers; domain doesn't reference API |
| `PersistenceConventionTests` | Every entity has a `DbSet`; value objects use `OwnsOne` not `DbSet`; enum properties configured; no static mutable state on `DbContext`; collection properties have private setters; migration scripts follow numbered prefix |
| `DapperConventionTests` | Query handlers must not use `SELECT *` in SQL (IL inspection of compiled string literals) |

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

### ~~1. Read Model Totals Are Never Written — CQRS Data Consistency Bug~~ FIXED

**Status: Resolved.** Dapper read queries now compute totals via `OUTER APPLY` subqueries against `OrderItems` instead of reading dead columns from the `Orders` table. The total columns (`TotalExcludingGst`, `TotalIncludingGst`, `TotalGstAmount`) have been dropped from the schema. Regression test `GetOrder_ShouldReturnCorrectTotals` verifies read-path totals match write-path totals.

### ~~2. No Authentication or Authorization~~ BY DESIGN

**Status: Intentional.** This template assumes the API runs behind an API gateway (API Management, YARP, nginx, etc.) that handles authentication and authorization. Embedding auth in the API would duplicate gateway concerns and couple the service to a specific identity provider.

Rate limiting and security headers provide defense-in-depth at the service level. If the API is ever exposed directly to clients without a gateway, add `AddAuthentication().AddJwtBearer()` with `RequireAuthorization()` on the endpoint groups.

### ~~3. CreateOrderCommand Has Two SaveChanges Without Transaction Boundary~~ FIXED

**Status: Resolved.** The root cause was a broken aggregate boundary: EF Core's `Items` navigation was `Ignore()`d, forcing items to be saved separately via `DbSet<OrderItem>`. Fix: restored the `Order→Items` navigation via backing field access (`UsePropertyAccessMode(PropertyAccessMode.Field)`), added `Order.AddItem(productId, name, qty, price, rate)` overload that constructs items through an `internal` OrderItem constructor (no orderId needed — EF sets FK on save). Handler now uses a single `SaveChangesAsync`. Regression test `CreateOrder_WithSecondProductNotFound_ShouldNotLeavePartialOrder` verifies no orphaned rows.

### ~~4. Public `SetId()` Methods Break Domain Encapsulation~~ FIXED

**Status: Resolved.** Removed `SetId()` from `Customer`, `Product`, and `OrderItem`. EF Core sets `Id` via the private setter. Deleted the corresponding unit tests that exercised these methods.

### ~~5. UpdateOrderStatus and CancelOrder Use AsNoTracking Then Update~~ FIXED

**Status: Resolved.** Both handlers now load tracked entities via `.Include(o => o.Items)`, mutate through domain methods, and call `SaveChangesAsync(cancellationToken)`. EF Core detects only changed properties — no more full-row overwrites. `Reconstitute` is no longer used in production handlers (made `internal`, retained for fuzz tests via `InternalsVisibleTo`).

### ~~6. Thin Application Layer~~ IMPROVED

**Status: Partially resolved.** `CreateOrderCommandHandler` now checks stock availability before adding each order item and decrements stock via `Product.UpdateStock()`. Stock reservation is atomic with order creation — if any item fails (product not found, insufficient stock), no stock is decremented and no order is saved. Three handler tests cover: insufficient stock rejection, stock decrement on success, and atomicity (second item failure leaves first product's stock unchanged).

**Remaining gap:** Other command handlers are still CRUD pass-through. `CancelOrderCommandHandler` now restores stock on cancellation, completing the stock lifecycle for the order flow.

### ~~7. Sparse Validation Coverage~~ FIXED

**Status: Resolved.** Every command and query now has an `IValidator<T>` implementation (16 total). Convention tests `EveryCommand_MustHaveAValidator` and `EveryQuery_MustHaveAValidator` enforce coverage — adding a new command or query without a validator fails the build.

Validators intentionally overlap with domain constructor guards (defense-in-depth). Validators provide structured multi-error `ValidationError` responses at the API boundary; domain guards are the safety net. The sync rule is documented in CLAUDE.md.

**Design rationale:** This codebase is AI-agent maintained. For human maintainers, requiring a validator for `DeleteProductCommand` (just `Id > 0`) would be busywork. For agents, the mechanical rule eliminates the judgment call "does this command need a validator?" — boilerplate is cheap, ambiguity is expensive.

### ~~8. Database Migrations Run on API Startup~~ FIXED

**Status: Resolved.** Removed `DatabaseMigrator.MigrateDatabase()` call from `Program.cs` and deleted the `DatabaseMigrator.cs` wrapper. Removed the DbMigrator project reference from the API `.csproj`. Migrations are now handled exclusively by the dedicated `DbMigrator` service:

- **Aspire:** `AppHost` runs `DbMigrator` as a standalone service with `WaitFor` dependency on SQL Server
- **Docker Compose:** New `migrator` service runs before the API via `condition: service_completed_successfully`. The `db` service has a health check so the migrator waits for SQL Server readiness
- **Standalone dev:** Run `dotnet run --project src/StarterApp.DbMigrator` before starting the API
- **Integration tests:** Unaffected — `TestFixture.RunDbUpMigrations()` runs migrations independently

The API Dockerfile no longer copies the DbMigrator project or its appsettings.json.

**Deployment note:** Any deployment pipeline that targets a real environment must run the migrator to completion before starting the API. The mechanism varies by platform (Kubernetes init container/Job, Azure Container Apps sidecar, AWS ECS essential container dependency with `"condition": "SUCCESS"`, or a CI/CD step running `dotnet run --project src/StarterApp.DbMigrator` with the target connection string). The Docker Compose setup is the reference pattern.

### ~~9. Money.Subtract Can Produce Negative Amounts~~ FIXED

**Status: Resolved.** `Subtract` now routes through `Create()` instead of the private constructor, so the existing `ThrowIfNegative` guard applies to all Money creation paths. Subtracting a larger amount from a smaller one throws `ArgumentOutOfRangeException`.

### 10. Missing Patterns for a Starter Template

**Severity: Low**

| Pattern | Impact |
|---------|--------|
| Domain events | No way to react to domain changes (e.g., "order created" > send email) |
| Outbox pattern | No reliable event publishing |
| Caching | No `IDistributedCache` or response caching |
| ~~`PagedResult<T>`~~ | ~~Endpoints accept `page`/`pageSize` but return raw collections without total count~~ — resolved. Endpoints now fetch `pageSize + 1` rows and set `X-Has-More` response header. Total count is a UI concern; APIs just signal whether more data exists. |
| API versioning | Routes use `/api/v1/` prefix strings but no formal versioning library |

**Recommendation:** Domain events are the highest-value addition. The custom mediator already has the infrastructure to dispatch them.

---

## Minor Issues

- ~~**Dockerfile installs SQL Server ODBC tools in production image**~~ — resolved. Removed ODBC tools and `mssql-tools18` from the runtime stage. The API uses `Microsoft.Data.SqlClient` (not ODBC); for `sqlcmd` debugging, use the `db` container directly.
- ~~**CI pipeline skips integration tests**~~ — resolved. A separate `integration` job now runs Testcontainers-based tests after the unit test job passes.
- ~~**CORS is fully permissive in development**~~ — resolved. Added comment clarifying intent: dev is permissive for local frontend testing; production blocks all browser cross-origin by default (secure for API-only use). To allow a browser SPA, configure `AllowedOrigins` in appsettings.
- ~~**`Email.IsValidEmail` uses try/catch for flow control**~~ — resolved. Now uses `MailAddress.TryCreate()` (available since .NET 8) to avoid exception-based flow control.
- ~~**No `appsettings.Development.json`**~~ — resolved. Added with `localhost` connection string defaults for standalone dev without Aspire.
- ~~**`Order.Reconstitute()` is public**~~ — now `internal`, visible only to the test assembly via `InternalsVisibleTo`.
- ~~**Scalar UI replaces Swagger UI**~~ — no longer relevant. Swashbuckle was removed from .NET 9+; Scalar is the standard replacement for OpenAPI UI.

---

## Test Coverage Summary

| Category | Files | What's Tested |
|----------|-------|---------------|
| Domain unit tests | 6 | Entity creation, validation, state transitions, value object behavior |
| Property-based (FsCheck) | 5 | Money arithmetic invariants, order state machine, GST calculations, email validation |
| Convention tests | 6 classes | Architecture boundaries, naming, CQRS separation, domain encapsulation, persistence mapping, Dapper SQL quality |
| Application tests | 7 | Command handler behavior with in-memory DbContext |
| Integration tests | 4+ | Full API endpoint testing with Testcontainers SQL Server, DbUp migrations, ProblemDetails responses |
| Test builders | 3 | Fluent builders for Customer, Product, Order |

**Coverage:** Every command handler now has targeted tests. All 9 handlers (Create/Update/Delete for Product and Customer, plus CreateOrder, UpdateOrderStatus, CancelOrder) have test classes covering successful operations, not-found exceptions, and domain invariant enforcement.

---

## Verdict

A well-engineered starter template that gets the hard things right: architecture enforcement through convention tests across 6 classes (including Dapper SELECT * prevention), proper CQRS separation, rich domain modeling with state machines and value objects, and modern DevOps with Aspire orchestration.

Issues #1, #3, #4, #5, #6, #7, #8, and #9 have been fixed or improved. The Order aggregate boundary is correct: items are managed through the aggregate root via `Order.AddItem()`, EF Core persists order + items atomically in a single `SaveChangesAsync`, and dead total columns have been dropped from the schema. Domain encapsulation is tightened: `SetId()` methods removed, `Reconstitute` made internal, command handlers use tracked entities with change detection, and `Money.Subtract` enforces the non-negative invariant. `CreateOrderCommandHandler` now validates stock availability and reserves inventory atomically with order creation. Every command and query has a validator, enforced by convention tests — a deliberate design choice for AI-agent maintenance where mechanical rules beat architectural taste. Database migrations are handled exclusively by the dedicated `DbMigrator` service across all deployment modes (Aspire, Docker Compose, standalone). Issue #2 (no auth) is intentional — the API assumes a gateway handles authentication. Remaining issue is (10) missing patterns (domain events, outbox, caching, API versioning). `PagedResult<T>` resolved with `X-Has-More` header pattern.

The convention tests remain the standout feature. They catch categories of architectural drift that code review alone would miss, and they scale as the codebase grows.

**Best suited for:** Teams starting a new .NET API who want architectural guardrails from day one and are willing to add auth and domain events themselves.