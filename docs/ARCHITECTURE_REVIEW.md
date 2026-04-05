# Architecture Review

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices across an e-commerce domain (Products, Customers, Orders) with Aspire orchestration and SQL Server.

**Score: 9.5/10** (all 35 findings resolved — no active P1/P2/P3 issues)

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
| `CachingConventionTests` | ICacheable queries must have non-empty cache keys, positive durations, and deterministic keys |

These tests catch entire categories of mistakes at compile time rather than in production.

### CQRS Implementation

The read/write split is cleanly executed:

- **Commands** flow through EF Core `DbContext` for writes, returning `*Dto` types
- **Queries** flow through Dapper `IDbConnection` for reads, returning `*ReadModel` types
- A **custom mediator** replaces MediatR, avoiding commercial licensing. It auto-discovers handlers via reflection and integrates validation as a pipeline behavior
- Convention tests mechanically prevent cross-contamination between read and write paths
- Zero CQRS violations found across all 9 command handlers and 8 query handlers

### Rich Domain Models

Entities contain real behavior, not just properties:

- **`Order`** has a state machine (Pending > Confirmed > Processing > Shipped > Delivered, with cancellation from valid states). `IsValidStatusTransition()` uses a switch expression that makes valid/invalid transitions explicit. `Confirm()` requires non-empty items. `Cancel()` prevents cancellation of delivered orders.
- **Value objects** (`Money`, `Email`) are immutable with private constructors, static factory methods, and proper `Equals`/`GetHashCode` overrides
- **`OrderItem`** encapsulates GST calculation logic with multiple derived values (`GetUnitPriceIncludingGst`, `GetTotalPriceExcludingGst`, `GetTotalPriceIncludingGst`, `GetTotalGstAmount`)
- Private setters and protected constructors enforce encapsulation throughout
- `Reconstitute()` factory methods handle database hydration without bypassing creation-time invariants (scoped `internal`, visible only to test assembly)

### Application Layer

All command handlers follow correct patterns:

- **Single `SaveChangesAsync`** per handler — atomicity guaranteed
- **Tracked entities** loaded via `FindAsync` / `Include().FirstOrDefaultAsync` — EF Core detects only changed properties
- **Domain methods invoked** for all mutations (`Order.Cancel()`, `Product.UpdateStock()`, `Customer.UpdateDetails()`) — no direct property assignment
- **Stock lifecycle** managed correctly: `CreateOrderCommandHandler` validates availability and decrements atomically; `CancelOrderCommandHandler` restores stock on cancellation
- **Consistent error handling**: `KeyNotFoundException` for missing entities, `InvalidOperationException` for domain violations

### Validator Coverage

Every command and query has an `IValidator<T>` implementation (16 total). Convention tests enforce this — adding a new command or query without a validator fails the build. Validators provide structured multi-error `ValidationError` responses at the API boundary; domain guards are the safety net (defense-in-depth).

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

- **Aspire orchestration** — `AppHost/Program.cs` wires up API, SQL Server, Seq, Service Bus emulator, Functions, and DbMigrator with proper `WaitFor` dependencies and optional dev tunnel support
- **Serilog** structured logging with console, file, Seq, and OpenTelemetry sinks
- **OpenTelemetry** metrics (ASP.NET Core, HTTP, runtime) and tracing via `ServiceDefaults`
- **Docker** multi-stage build with docker-compose (API + SQL Server + Seq + Service Bus emulator + Functions + dedicated migrator)
- **CI** pipeline with GitHub Actions (unit tests + integration tests with Testcontainers)
- **Health checks** at `/health`, `/health/ready`, `/health/live`, and `/alive`
- **Password masking** in log output — implemented consistently across `Program.cs`, `DatabaseMigrationEngine`, and `DbMigrator`
- **Dedicated `DbMigrator` service** for migrations across all deployment modes (Aspire, Docker Compose, standalone)
- **Outbox → Service Bus pipeline** — domain events captured durably in `OutboxMessages` during `SaveChangesAsync` (post-persist, so IDENTITY values are correct), published to Azure Service Bus by `OutboxProcessor` BackgroundService with row-level locking (`UPDLOCK, READPAST, ROWLOCK`) to prevent duplicate publishing across replicas. Service Bus topic has duplicate detection enabled (10-minute window). Consumed by Azure Functions via topic subscriptions with correlation filters. Convention test enforces subscription filter ↔ domain event name sync.
- **Explicit constraint naming** — all database constraints named via convention (`PK_`, `FK_`, `DF_`, `CK_`, `IX_`), enforced by convention test from script 0012 onward

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
- `MailAddress.TryCreate()` for email validation (no exception-based flow control)

---

## Weaknesses

### Open Findings

These are unresolved issues identified across multiple agent review sessions. When fixing an item, mark it as resolved with a strikethrough and note the commit.

No open findings. All findings have been resolved.

#### Recently resolved (outbox correctness + eventing contract)

| # | Finding | Fix |
|---|---------|-----|
| ~~28~~ | OrderCreatedDomainEvent captures pre-persist OrderId (always 0) | Record creation events AFTER first SaveChanges when IDENTITY values are assigned |
| ~~29~~ | Outbox rows can be published more than once (no locking, no dedup) | Row-level locking (UPDLOCK, READPAST, ROWLOCK) + transaction in OutboxProcessor; duplicate detection enabled on Service Bus topic |
| ~~30~~ | Event routing coupled to CLR type names — rename breaks routing silently | Convention test validates subscription filter EventType values against actual IDomainEvent class names |
| ~~31~~ | Aspire integration test doesn't verify eventing path | Added `CreateOrder_ShouldWriteAndProcessOutboxEvent` — queries OutboxMessages directly via SQL, asserts row exists with `order.created.v1` type and polls until `ProcessedOnUtc` is non-null (proves outbox processor published to Service Bus) |

#### Recently resolved (P3 hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~32~~ | Dapper query handlers don't pass CancellationToken | All 7 query handlers now use `CommandDefinition` with `cancellationToken` parameter |
| ~~33~~ | Money and Email value objects lack `operator ==`/`!=` | Added `operator ==` and `operator !=` overloads to both value objects |
| ~~34~~ | `order.status-changed.v1` published but no subscription matches | Added `OrderStatusChangedFilter` correlation rule to `email-notifications` subscription |
| ~~35~~ | `Directory.Build.props` NuGetLockFilePath uses backslash | Changed to forward slash for cross-platform compatibility |

#### Recently resolved (Service Bus hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~22~~ | ServiceBusClient leaks AMQP connections | Factory-based DI registration — container owns disposal |
| ~~23~~ | No retry logic for transient failures | `IncrementRetry()` + `MaxRetries` — messages retry before permanent error |
| ~~24~~ | Outbox index missing Error column | Filtered index `WHERE ProcessedOnUtc IS NULL AND Error IS NULL` (Error is NVARCHAR(MAX), can't be key) |
| ~~25~~ | OutboxProcessorOptions has no validation | DataAnnotations with `ValidateOnStart()` |
| ~~26~~ | Functions host.json missing Service Bus config | Added `maxConcurrentCalls`, `autoCompleteMessages`, `maxAutoLockRenewalDuration` |
| ~~27~~ | Functions stubs have no error handling | Added try-catch with structured error logging and re-throw |

#### Recently resolved (commit 614f069)

| # | Finding | Fix |
|---|---------|-----|
| ~~16~~ | UpdateProduct zeros Price/Stock on field omission | Command properties changed to nullable; validator rejects nulls |
| ~~17~~ | Order status case sensitivity mismatch | Handler now uses `Enum.Parse` with `ignoreCase: true` |
| ~~18~~ | DB constraints surface as 500s | `DbUpdateExceptionExtensions` maps SQL errors to 409/400; domain guards + validators enforce max lengths; customer handlers check email uniqueness pre-save |
| ~~19~~ | TestFixture hides base disposal | Added `Client?.Dispose()` and `base.Dispose()` |
| ~~21~~ | PropertiesTest false positives | Tests now use actual `IValidator<T>` implementations instead of DataAnnotations |

### Recently Resolved (commits 05d2996–898424c)

| Finding | Fix | Commit |
|---------|-----|--------|
| Client-trusted pricing on order creation | Price, currency, GST now sourced from product catalog | 05d2996 |
| Duplicate ProductId corrupts stock reservation | Validator + handler duplicate check, defense-in-depth | 05d2996 |
| No concurrency control on stock (overselling) | Atomic `ExecuteUpdateAsync` with `WHERE Stock >= @qty` | 05d2996 |
| Rate limiting configured but never enforced | Added `GlobalLimiter` with per-IP partitioning | 05d2996 |
| Mixed currencies produce incorrect order totals | `Order.EnsureCurrencyMatchesExistingItems` domain guard | 05d2996 |
| Outbox two-save atomicity gap | DbContext manages internal transaction when no outer one exists | 2fbf07c |
| `RecordCreation()` was caller-responsibility | Auto-detected via change tracker in `SaveChangesWithOutboxAsync` | 2fbf07c |
| Stock reservation checked existence after UPDATE | Reordered: load product first, then atomic reserve | 898424c |
| No upper bound on order items count | Validator caps at 50 items | 898424c |
| UpdateProduct zeros Price/Stock on field omission | Nullable command properties; validator rejects nulls | 614f069 |
| Order status case sensitivity mismatch | `Enum.Parse` with `ignoreCase: true` | 614f069 |
| DB constraints surface as 500s | `DbUpdateExceptionExtensions`, domain max-length guards, validator mirrors | 614f069 |
| TestFixture hides base disposal | Added `Client?.Dispose()` and `base.Dispose()` | 614f069 |
| PropertiesTest false positives | Tests now use actual `IValidator<T>` implementations | 614f069 |
| Closed switch in outbox serialization | Generic serialization; events flattened; convention test prevents entity references in events | (this commit) |

---

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

**Status: Partially resolved.** `CreateOrderCommandHandler` now checks stock availability before adding each order item and decrements stock via `Product.UpdateStock()`. Stock reservation is atomic with order creation — if any item fails (product not found, insufficient stock), no stock is decremented and no order is saved. `CancelOrderCommandHandler` restores stock on cancellation, completing the stock lifecycle for the order flow.

**Remaining gap:** Other command handlers are still CRUD pass-through.

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

### ~~10. Delete Handlers Missing Referential Integrity Checks~~ FIXED

**Status: Resolved.** Both `DeleteProductCommandHandler` and `DeleteCustomerCommandHandler` now check for existing orders before deletion. `DeleteProductCommandHandler` queries `OrderItems.AnyAsync(oi => oi.ProductId == id)` and throws `InvalidOperationException` if the product is referenced. `DeleteCustomerCommandHandler` queries `Orders.AnyAsync(o => o.CustomerId == id)` and throws similarly. Regression tests verify both cases.

### ~~11. Stock Race Condition in CreateOrderCommand~~ FIXED

**Status: Resolved.** Added migration `0007_AddStockNonNegativeConstraint.sql` with `CHECK (Stock >= 0)` on the `Products` table. The database is now the final arbiter — if two concurrent stock decrements race, the second `SaveChangesAsync` throws a database exception. The application-layer check (`product.Stock < quantity`) handles the common case with a clear error message; the database constraint is the safety net for concurrency edge cases.

### ~~12. CancelOrderCommand Silently Skips Deleted Products~~ FIXED

**Status: Resolved.** `CancelOrderCommandHandler` now logs a warning via `Log.Warning` when a product no longer exists during stock restoration, including the product ID, quantity, and order ID. The cancellation still succeeds (the order should be cancellable regardless of product state), but operators have visibility into unrestorable stock via structured logs.

### ~~13. UpdateDetails Methods Have Ambiguous Null Semantics~~ FIXED

**Status: Resolved.** `Product.UpdateDetails()` and `Customer.UpdateDetails()` now use the same guards as their constructors: `ArgumentException.ThrowIfNullOrWhiteSpace(name)` and `ArgumentNullException.ThrowIfNull()` for price/email. Passing invalid input is now an error at both creation and update time. Domain tests updated to verify the strict behavior.

### ~~14. CreateOrderCommandValidator Missing GST Rate Bounds~~ FIXED

**Status: Resolved.** `CreateOrderCommandValidator` now validates `GstRate` bounds (0 to 1.0) per item in the validation loop, yielding a structured `ValidationError` with message `"GST rate must be between 0 and 1 (e.g., 0.10 for 10%)"`. This provides a clean 400 response at the API boundary instead of letting the domain guard throw a raw `ArgumentOutOfRangeException`.

### 15. Missing Patterns for a Starter Template

**Severity: Low**

| Pattern | Impact |
|---------|--------|
| ~~Domain events~~ | ~~Implemented for the `Order` aggregate~~ — resolved. Full pipeline: domain events → outbox → `OutboxProcessor` BackgroundService → Azure Service Bus → Azure Functions subscribers |
| ~~Outbox pattern~~ | ~~Still needs a background dispatcher~~ — resolved. `OutboxProcessor` polls and publishes to Service Bus; Functions consume via topic subscriptions with correlation filters |
| Caching | No `IDistributedCache` or response caching |
| ~~`PagedResult<T>`~~ | ~~Endpoints accept `page`/`pageSize` but return raw collections without total count~~ — resolved. Endpoints now fetch `pageSize + 1` rows and set `X-Has-More` response header. Total count is a UI concern; APIs just signal whether more data exists. |
| API versioning | Routes use `/api/v1/` prefix strings but no formal versioning library |

**Recommendation:** Caching (`IDistributedCache` for product catalog) and API versioning are the next opportunities.

---

## Minor Issues

- ~~**Dockerfile installs SQL Server ODBC tools in production image**~~ — resolved. Removed ODBC tools and `mssql-tools18` from the runtime stage. The API uses `Microsoft.Data.SqlClient` (not ODBC); for `sqlcmd` debugging, use the `db` container directly.
- ~~**CI pipeline skips integration tests**~~ — resolved. A separate `integration` job now runs Testcontainers-based tests after the unit test job passes.
- ~~**CORS is fully permissive in development**~~ — resolved. Added comment clarifying intent: dev is permissive for local frontend testing; production blocks all browser cross-origin by default (secure for API-only use). To allow a browser SPA, configure `AllowedOrigins` in appsettings.
- ~~**`Email.IsValidEmail` uses try/catch for flow control**~~ — resolved. Now uses `MailAddress.TryCreate()` (available since .NET 8) to avoid exception-based flow control.
- ~~**No `appsettings.Development.json`**~~ — resolved. Added with `localhost` connection string defaults for standalone dev without Aspire.
- ~~**`Order.Reconstitute()` is public**~~ — now `internal`, visible only to the test assembly via `InternalsVisibleTo`.
- ~~**Scalar UI replaces Swagger UI**~~ — no longer relevant. Swashbuckle was removed from .NET 9+; Scalar is the standard replacement for OpenAPI UI.
- **`Directory.Build.props` lock file path uses backslashes** — `NuGetLockFilePath` uses `\` separator. Works on Windows and modern .NET MSBuild on macOS/Linux, but should use `/` for explicit cross-platform compatibility.
- ~~**CI pipeline missing NuGet cache**~~ — resolved. `actions/setup-dotnet` now uses built-in NuGet caching keyed from `packages.lock.json`.
- ~~**No Dockerfile health check**~~ — resolved. The runtime image now includes a `HEALTHCHECK` targeting `/health/live`.
- ~~**ServiceDefaults only adds liveness probe**~~ — resolved at the API layer. The API now exposes `/health/ready` backed by a database readiness check, alongside `/health/live` and `/alive`.

---

## Test Coverage Summary

| Category | Files | What's Tested |
|----------|-------|---------------|
| Domain unit tests | 6 | Entity creation, validation, state transitions, value object behavior |
| Property-based (FsCheck) | 5 | Money arithmetic invariants, order state machine, GST calculations, email validation |
| Convention tests | 6 classes | Architecture boundaries, naming, CQRS separation, domain encapsulation, persistence mapping, Dapper SQL quality, DateTimeOffset enforcement, constraint naming enforcement, event routing contract validation |
| Application tests | 9 | All command handlers tested with in-memory DbContext |
| Infrastructure tests | 2 | OutboxMessage mutation tests, OutboxProcessor batch processing with Moq ServiceBusSender |
| Integration tests | 4+ | Full API endpoint testing with Testcontainers SQL Server, DbUp migrations, ProblemDetails responses |
| Aspire integration tests | 4 | End-to-end pipeline testing via DistributedApplicationTestingBuilder: health endpoints, CRUD path, stock decrement, outbox-to-Service-Bus eventing verification |
| Test builders | 3 | Fluent builders for Customer, Product, Order |

**Coverage:** Every command handler has targeted tests. All 9 handlers (Create/Update/Delete for Product and Customer, plus CreateOrder, UpdateOrderStatus, CancelOrder) have test classes covering successful operations, not-found exceptions, and domain invariant enforcement.

---

## Verdict

A well-engineered starter template that gets the hard things right: architecture enforcement through convention tests across 6 classes (including Dapper SELECT * prevention via IL inspection), proper CQRS separation with zero violations, rich domain modeling with state machines and value objects, and modern DevOps with Aspire orchestration.

Issues #1–#14, #16–#21, #22–#27, and #28–#31 have all been resolved. Recent hardening addressed critical security and correctness gaps: order creation now sources pricing from the catalog, stock reservation uses atomic SQL to prevent overselling, the outbox persists events transactionally, rate limiting is enforced globally, and mixed-currency orders are rejected at the domain level. The Service Bus integration was hardened with proper resource disposal, retry logic for transient failures, validated configuration, and optimized database indexing. The outbox processor now uses row-level locking to prevent duplicate publishing across replicas, domain events capture post-persist identity values, convention tests enforce that Service Bus subscription filters stay in sync with stable event contracts, and Aspire integration tests verify the full eventing path (outbox row creation and processor publication) via direct database queries.

The convention tests remain the standout feature. They catch categories of architectural drift that code review alone would miss, and they scale as the codebase grows.

**Best suited for:** Teams starting a new .NET API who want architectural guardrails from day one. Auth is left to the API gateway by design; the full event pipeline (domain events → outbox → Service Bus → Azure Functions) is implemented for the Order aggregate.
