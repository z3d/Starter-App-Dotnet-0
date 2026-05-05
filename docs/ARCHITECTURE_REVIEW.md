# Architecture Review

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices across an e-commerce domain (Products, Customers, Orders) with Aspire orchestration and SQL Server.

**Score: 9.8/10** (53 findings resolved, 3 open)

---

## Strengths

### Clean Architecture with Convention-Enforced Boundaries

The project enforces architectural rules through convention tests in the main and AppHost test projects using Best.Conventional plus targeted reflection/source scanners. This is the strongest feature of the codebase — architectural decisions are not just documented but mechanically verified on every test run.

| Test Class | What It Enforces |
|------------|-----------------|
| `NamingConventionTests` | Endpoints, DTOs, commands, queries, handlers, validators, services, and test classes follow naming conventions; application contracts and handlers live in mechanically discoverable namespaces |
| `CqrsConventionTests` | Command handlers don't touch `IDbConnection`; query handlers don't touch `DbContext`; every command/query has a handler; dual interface enforcement (`ICommand` + `IRequest<T>`) |
| `DomainConventionTests` | Private property setters on entities; immutable value objects; public getters on DTOs; non-public default constructors; `Equals`/`GetHashCode` overrides; async suffix; no async void; no `DateTime.Now`; aggregate creation-event safety |
| `ApiConventionTests` | Endpoints don't access DB directly; protected API groups require gateway identity metadata; raw gateway identity headers stay behind infrastructure abstractions; validators are pure; DTOs have no instance methods; API contract shapes are serializer-friendly; collection properties are materialized; mappers are static; handlers don't dispatch to other handlers; domain doesn't reference API or third-party packages |
| `PersistenceConventionTests` | Every entity has a `DbSet`; value objects use `OwnsOne` not `DbSet`; enum properties configured; no static mutable state on `DbContext`; collection properties have private setters; migration scripts follow numbered prefix, are embedded resources, and name constraints explicitly |
| `DapperConventionTests` | Query handlers must not use `SELECT *` in SQL (IL inspection of compiled string literals) |
| `CachingConventionTests` | ICacheable queries must have non-empty cache keys, positive durations, and deterministic keys |
| `HousekeepingConventionTests` | Project files don't reference `bin`/`obj` artifacts; production code avoids regions, XML documentation comments, and historical workaround comments; Docker Compose mirrors Aspire payload-archive storage wiring |
| `StarterApp.AppHost.Tests` conventions | Service Bus topology/Function trigger alignment plus async/DateTime safety for AppHost, Functions, and ServiceDefaults |

These tests catch entire categories of mistakes at compile time rather than in production.

### Advisory Consistency Measurement

The test suite now includes a `Consistency/` layer that measures structural drift without turning every pattern into a hard rule. It tracks three extensible cohorts against pinned exemplars in `docs/exemplars/`:

- Command handlers: dependencies, logging, cache invalidation, entity loads, helper methods, and control-flow shape
- Query handlers: caching, list/paged/by-id shape, SQL statement count, joins, parameters, and dependency count
- EF configurations: relationship/value-object/index/conversion mapping shape

This complements the convention tests: deterministic "must always" policies stay in Best.Conventional tests, while consistency tests surface advisory outliers for review.

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
- **Stock lifecycle** managed correctly: `CreateOrderCommandHandler` validates availability and decrements atomically; both cancellation endpoints share one stock-restoration workflow
- **Consistent error handling**: `KeyNotFoundException` for missing entities, `InvalidOperationException` for domain violations

### Validator Coverage

Every command and query has an `IValidator<T>` implementation (16 total). Convention tests enforce this — adding a new command or query without a validator fails the build. Validators provide structured multi-error `ValidationError` responses at the API boundary via the ProblemDetails `errors` extension; domain guards are the safety net (defense-in-depth).

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

### Trusted Gateway Identity

Authentication remains gateway-owned, but the API now has an enforceable trust boundary instead of blind header trust:

- `/api/v1` endpoint groups opt into `RequireGatewayIdentity()` metadata; health and OpenAPI stay public
- `GatewayIdentityMiddleware` parses a small normalized header contract into `ICurrentUser`
- Production-like `GatewayIdentity:Mode=Required` validates a signed `X-Gateway-Assertion` over issuer, audience, subject, principal type, tenant, scopes, correlation id, method, path, short lifetime, key id, and a hash of the projected headers
- Local Development/Testing/Docker can use `UnsignedDevelopment`, and startup validation rejects that mode elsewhere
- Security tests cover missing identity, missing assertion, expired assertion, wrong audience, wrong path, tampered identity headers, wrong signing key, and unknown key id
- Rate limiting now partitions protected requests by verified tenant/subject identity, falling back to IP only where no authenticated gateway identity exists

### DevOps and Observability

- **Aspire orchestration** — `AppHost/Program.cs` wires up API, SQL Server, Redis, Blob storage, Seq, Service Bus emulator, Functions, and DbMigrator with proper `WaitFor` dependencies and optional dev tunnel support
- **Serilog** structured logging with console, file, Seq, and OpenTelemetry sinks
- **OpenTelemetry** metrics (ASP.NET Core, HTTP, runtime) and tracing via `ServiceDefaults`
- **Docker** multi-stage build with docker-compose (API + SQL Server + Redis + Azurite + Seq + Service Bus emulator + Functions + dedicated migrator)
- **CI** pipeline with GitHub Actions (unit tests + integration tests with Testcontainers)
- **Health checks** at `/health`, `/health/ready`, `/health/live`, and `/alive`
- **Password masking** in log output — implemented consistently across `Program.cs`, `DatabaseMigrationEngine`, and `DbMigrator`
- **Payload archive / PII audit** — HTTP request/response bodies, outbound Service Bus payloads, and inbound Function payloads are written as JSONL support artifacts to Azure Blob under date/hour/minute paths. Archive files are correlation-bound (`archive/{date}/{hour}/{minute}/{correlationId}.jsonl`); audit files are time-window streams (`audit/{date}/{hour}/{minute}/payload-audit.jsonl`) that include timestamp, correlation id, archive blob name, payload hash, payload bounds metadata, and the captured payload. HTTP capture is bounded by `MaxPayloadBytes` and content-type allowlist metadata; Service Bus payload capture remains full-fidelity for JSON events. Operational logs use redacted payloads and a convention test blocks direct raw `{Body}` logging.
- **Dedicated `DbMigrator` service** for migrations across all deployment modes (Aspire, Docker Compose, standalone)
- **Outbox → Service Bus pipeline** — domain events captured durably in `OutboxMessages` during a single `SaveChangesAsync` (aggregates use client-generated Guid v7 Ids, so creation events carry correct keys before the save). `EnableRetryOnFailure` is safe because no user transaction is needed. Rows are published to Azure Service Bus by `OutboxProcessor` BackgroundService with row-level locking (`UPDLOCK, READPAST, ROWLOCK`) to prevent duplicate publishing across replicas. Service Bus topic has duplicate detection enabled (10-minute window). Consumed by Azure Functions via topic subscriptions with correlation filters. Convention test enforces subscription filter ↔ domain event contract sync.
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

Fresh review reconciliation: finding #43 is resolved by the trusted gateway identity boundary and identity-based rate limiting. Findings #44 and #45 remain open from earlier review sessions. Finding #56 records an order-state modeling design risk: the enum is acceptable as a compact state label, but workflows should not grow around arbitrary status assignment. The other fresh-eyes findings were fixed in commit `5285814` and recorded below as #52-#55.

| # | Finding | Impact | Suggested Fix |
|---|---------|--------|---------------|
| 44 | `OutboxProcessor` holds SQL update locks while sending to Service Bus | Slow or throttled broker calls make the database transaction span network I/O and can create lock pressure under load | Claim rows in a short transaction with `ProcessingId`/`LockedUntil`, publish outside the transaction, then mark processed in a second short transaction |
| 45 | AppHost eventing test observes `ProcessedOnUtc` but not subscriber consumption | Service Bus filters, Functions triggers, or subscriber host wiring can break while the test still passes after the sender marks the outbox row processed | Add a durable subscriber effect, test sink, or subscription-drain assertion so the consumer side is observable |
| 56 | Order status API models lifecycle changes as arbitrary state assignment | `OrderStatus` is fine as a finite state label, and the aggregate owns transition validation, but `UpdateOrderStatusCommand.Status` accepts a string and the handler parses it into a generic `UpdateStatus(...)` call. As order behavior grows, this shape encourages state-specific side effects to leak into handlers, as cancellation already needs special stock-restoration handling. | Keep the enum for persistence/API readability, but prefer intent-specific commands and domain methods (`Confirm`, `StartProcessing`, `Ship`, `Deliver`, `Cancel`) over "set status" APIs. Normalize query input to `OrderStatus` before SQL, and keep state-specific side effects behind aggregate/domain-service operations. |

#### Recently resolved (gateway identity hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~43~~ | Rate limiting partitions by `RemoteIpAddress` without trusted forwarded-header handling | Added a trusted gateway identity contract with signed assertion validation, `ICurrentUser`, `RequireGatewayIdentity()` endpoint metadata, convention coverage for protected API groups and raw header access, and identity-based rate-limit partitioning for protected requests. |

#### Recently resolved (fresh review fixes, commit 5285814)

| # | Finding | Fix |
|---|---------|-----|
| ~~52~~ | `PUT /orders/{id}/status` could cancel an order without restoring reserved stock | Added shared `OrderCancellationService` used by both `CancelOrderCommandHandler` and `UpdateOrderStatusCommandHandler`; added regression coverage for cancelling through the status-update path restoring stock. |
| ~~53~~ | `ValidationException` collected structured `ValidationError`s, but HTTP responses returned generic ProblemDetails only | Replaced plain `AddProblemDetails()` with `AddApiProblemDetails()` customization that emits `errors` grouped by property; added fast ProblemDetails customization coverage and an integration assertion. |
| ~~54~~ | Domain project referenced Serilog despite the no-external-dependencies rule, and conventions only blocked API references | Removed the Domain Serilog package reference and lock-file entry; added `DomainAssembly_MustNotReferenceThirdPartyAssemblies` convention coverage. |
| ~~55~~ | `OrderItem` rejected GST rates above 1.0, but the database schema only checked `GstRate >= 0` | Added DbUp migration `0017_AddOrderItemGstRateRangeConstraint.sql` with named constraint `CK_OrderItems_GstRate_Range` enforcing `0 <= GstRate <= 1`. |

#### Recently resolved (payload archive hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~46~~ | Payload capture had no explicit failure policy, so configured storage outages broke requests while missing storage silently dropped archive rows | Added `PayloadCapture:FailureMode=FailOpen|FailClosed`, `RequireArchiveStore` startup validation, null-store skip logging, and tests for fail-open/fail-closed behavior. Aspire and Docker run production-like payload capture with `RequireArchiveStore=true` and `FailClosed`. |
| ~~47~~ | Docker Compose did not mirror Aspire's payload archive Blob dependency | Added Azurite to Docker Compose, wired API and Functions `payloadarchive` connection strings, persisted `azurite-data`, and added a convention test that fails if Docker loses this wiring. |
| ~~48~~ | HTTP payload capture buffered full request/response bodies with no limit | Replaced full response buffering with a bounded tee stream, bounded request reads by `PayloadCapture:MaxPayloadBytes`, added content-type capture rules, and persisted truncation/skip metadata on archive/audit rows. |
| ~~49~~ | JSON log redaction only matched exact sensitive property names and missed emails inside JSON strings | Redactor now matches normalized sensitive names inside property names (e.g. `customerEmail`, `ownerName`) and redacts email-like substrings inside non-sensitive JSON string values. |
| ~~50~~ | API console logging was configured twice | Removed the unconditional code-level console sink from `Program.cs`; Serilog sinks now come from configuration. |
| ~~51~~ | `PayloadCaptureOptions.CleanupCron` was exposed but the Function timer was hardcoded | `PayloadArchiveCleanupFunction` now uses the `PayloadCapture__CleanupCron` app setting; local, Docker, and AppHost configurations provide the default hourly schedule. |

#### Recently resolved (retry and concurrency hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~41~~ | `CreateOrderCommandHandler` generated a fresh order id inside the execution-strategy retry delegate, so a commit-unknown retry could create a second order and reserve stock twice | The handler now generates one stable order id before the retry delegate and checks for that id at the start of each retry before reserving stock. `Order` has an internal explicit-id constructor for this retry-safe path. |
| ~~42~~ | Cancellation restored stock with no stale-write gate, allowing concurrent cancellation/status changes to double-restore or overwrite inventory | `Order` and `Product` now have SQL Server `rowversion` concurrency tokens configured with `IsRowVersion()`, and `DbUpdateConcurrencyException` maps to `409 Conflict`. A convention test keeps those tokens in place. |

#### Recently resolved (Azure SQL transient retry)

| # | Finding | Fix |
|---|---------|-----|
| ~~40~~ | Conventional.Samples comparison exposed convention coverage gaps around migration embedding, serializer-friendly response contracts, namespace locality, collection materialization, bin/obj project references, comment hygiene, and async/time scan scope | Added focused convention tests for these rules in `StarterApp.Tests` and `StarterApp.AppHost.Tests`; converted existing production XML documentation comments to short ordinary comments; left EF value-object default-constructor initialization intentionally out of scope. |
| ~~39~~ | No advisory consistency layer for structural drift across common file shapes | Added `StarterApp.Tests/Consistency/` with command-handler, query-handler, and EF-configuration cohorts; added pinned exemplar docs under `docs/exemplars/`; extracted EF mappings into per-entity `IEntityTypeConfiguration<T>` classes so mapping shape is measurable outside `ApplicationDbContext`. |
| ~~36~~ | No transient-failure retry on `DbContext` for Azure SQL throttling / failover | `Order` aggregate now uses client-generated `Guid.CreateVersion7()` IDs. This lets `RecordCreation()` run BEFORE `SaveChanges` (events already know their Ids), keeping outbox capture inside a single `SaveChanges` — no user transaction needed. `EnableRetryOnFailure(6, 30s)` is re-enabled in `AddPersistence`. `CreateOrderCommandHandler` (the one handler that still needs a user-managed transaction for atomic stock-reservation + order-save) wraps itself in `Database.CreateExecutionStrategy().ExecuteAsync(...)` and calls `ChangeTracker.Clear()` at the top of each attempt so retries start from a clean state. New convention test enforces: any `AggregateRoot` overriding `RecordCreation()` must have a `Guid Id` — if a future aggregate tries to raise creation events from an IDENTITY PK, the build fails. Migration `0014_ConvertOrderIdToGuid.sql` converts existing `Orders.Id` and `OrderItems.OrderId` to `UNIQUEIDENTIFIER`. |
| ~~38~~ | Dapper query handlers had no transient-fault retry — `EnableRetryOnFailure` only covers the EF `DbContext`; Dapper creates its own `SqlCommand` with `RetryLogicProvider = null`, so a mid-query failover or throttling event on Azure SQL would surface as a 500 while writes are transparently retried | New `SqlRetryPolicy.ExecuteAsync` helper at `Infrastructure/Persistence/SqlRetryPolicy.cs` retries transient `SqlException`s with exponential backoff (6 attempts, 30s cap, base 1s) using the same transient-error numbers as EF's `SqlServerRetryingExecutionStrategy`. All 7 query handlers (`GetAllProducts`, `GetProductById`, `GetCustomer`, `GetCustomers`, `GetOrderById`, `GetOrdersByStatus`, `GetOrdersByCustomer`) now wrap their Dapper calls in the helper. New convention test `DapperConventionTests.QueryHandlers_MustUseSqlRetryPolicy` scans IL and fails the build if any future `*QueryHandler` with an `IDbConnection` field forgets to go through the helper. |

#### Recently resolved (outbox publish resilience)

| # | Finding | Fix |
|---|---------|-----|
| ~~37~~ | Transient Service Bus outages consumed per-message retry budget — a multi-minute SB outage would mark every polled message as permanently `Error`, requiring manual requeue | `OutboxProcessor` now distinguishes transient `ServiceBusException` reasons (`ServiceCommunicationProblem`, `ServiceTimeout`, `ServiceBusy`, `QuotaExceeded`) from message-level failures. Transient errors log a warning, break the batch, and leave rows unprocessed with retries intact — next poll tick re-attempts cleanly. Message-level errors (e.g. `MessageSizeExceeded`) still consume retries. No dedicated circuit breaker — the outbox already decouples user requests from publish latency, and this targeted fix addresses the actual failure mode. |

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
| `RecordCreation()` was caller-responsibility | Auto-detected via change tracker during `SaveChanges` | 2fbf07c |
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

**Status: Intentional and hardened.** This template assumes the API runs behind APIM or an equivalent trusted gateway that validates caller authentication. The API does not add ASP.NET authentication/JWT bearer middleware, but it no longer blindly trusts arbitrary headers.

Protected API groups require normalized gateway identity headers and, in production-like `GatewayIdentity:Mode=Required`, a signed `X-Gateway-Assertion`. The API exposes identity through `ICurrentUser`, rejects missing/tampered assertions with `401`, and keeps resource/tenant authorization in application/domain code.

### ~~3. CreateOrderCommand Has Two SaveChanges Without Transaction Boundary~~ FIXED

**Status: Resolved.** The root cause was a broken aggregate boundary: EF Core's `Items` navigation was `Ignore()`d, forcing items to be saved separately via `DbSet<OrderItem>`. Fix: restored the `Order→Items` navigation via backing field access (`UsePropertyAccessMode(PropertyAccessMode.Field)`), added `Order.AddItem(productId, name, qty, price, rate)` overload that constructs items through an `internal` OrderItem constructor (no orderId needed — EF sets FK on save). Handler now uses a single `SaveChangesAsync`. Regression test `CreateOrder_WithSecondProductNotFound_ShouldNotLeavePartialOrder` verifies no orphaned rows.

### ~~4. Public `SetId()` Methods Break Domain Encapsulation~~ FIXED

**Status: Resolved.** Removed `SetId()` from `Customer`, `Product`, and `OrderItem`. EF Core sets `Id` via the private setter. Deleted the corresponding unit tests that exercised these methods.

### ~~5. UpdateOrderStatus and CancelOrder Use AsNoTracking Then Update~~ FIXED

**Status: Resolved.** Both handlers now load tracked entities via `.Include(o => o.Items)`, mutate through domain methods, and call `SaveChangesAsync(cancellationToken)`. EF Core detects only changed properties — no more full-row overwrites. `Reconstitute` is no longer used in production handlers (made `internal`, retained for fuzz tests via `InternalsVisibleTo`).

### ~~6. Thin Application Layer~~ IMPROVED

**Status: Partially resolved.** `CreateOrderCommandHandler` now checks stock availability before adding each order item and decrements stock via `Product.UpdateStock()`. Stock reservation is atomic with order creation — if any item fails (product not found, insufficient stock), no stock is decremented and no order is saved. A shared `OrderCancellationService` restores stock for both the dedicated cancel command and the status-update cancellation path.

**Remaining gap:** Other command handlers are still CRUD pass-through.

### ~~7. Sparse Validation Coverage~~ FIXED

**Status: Resolved.** Every command and query now has an `IValidator<T>` implementation (16 total). Convention tests `EveryCommand_MustHaveAValidator` and `EveryQuery_MustHaveAValidator` enforce coverage — adding a new command or query without a validator fails the build.

Validators intentionally overlap with domain constructor guards (defense-in-depth). Validators provide structured multi-error `ValidationError` responses at the API boundary through the ProblemDetails `errors` extension; domain guards are the safety net. The sync rule is documented in AGENTS.md and CLAUDE.md.

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

**Status: Resolved.** The shared cancellation service logs a warning via `Log.Warning` when a product no longer exists during stock restoration, including the product ID, quantity, and order ID. The cancellation still succeeds (the order should be cancellable regardless of product state), but operators have visibility into unrestorable stock via structured logs.

### ~~13. UpdateDetails Methods Have Ambiguous Null Semantics~~ FIXED

**Status: Resolved.** `Product.UpdateDetails()` and `Customer.UpdateDetails()` now use the same guards as their constructors: `ArgumentException.ThrowIfNullOrWhiteSpace(name)` and `ArgumentNullException.ThrowIfNull()` for price/email. Passing invalid input is now an error at both creation and update time. Domain tests updated to verify the strict behavior.

### ~~14. CreateOrderCommandValidator Missing GST Rate Bounds~~ FIXED

**Status: Resolved.** `CreateOrderCommandValidator` now validates `GstRate` bounds (0 to 1.0) per item in the validation loop, yielding a structured `ValidationError` with message `"GST rate must be between 0 and 1 (e.g., 0.10 for 10%)"`. This provides a clean 400 response at the API boundary instead of letting the domain guard throw a raw `ArgumentOutOfRangeException`. Migration `0017_AddOrderItemGstRateRangeConstraint.sql` also enforces the same range at the database boundary.

### 15. Missing Patterns for a Starter Template

**Severity: Low**

| Pattern | Impact |
|---------|--------|
| ~~Domain events~~ | ~~Implemented for the `Order` aggregate~~ — resolved. Full pipeline: domain events → outbox → `OutboxProcessor` BackgroundService → Azure Service Bus → Azure Functions subscribers |
| ~~Outbox pattern~~ | ~~Still needs a background dispatcher~~ — resolved. `OutboxProcessor` polls and publishes to Service Bus; Functions consume via topic subscriptions with correlation filters |
| ~~Caching~~ | ~~Redis-backed `IDistributedCache` support with by-id query caching and command invalidation~~ — resolved |
| ~~`PagedResult<T>`~~ | ~~Endpoints accept `page`/`pageSize` but return raw collections without total count~~ — resolved. Endpoints now fetch `pageSize + 1` rows and set `X-Has-More` response header. Total count is a UI concern; APIs just signal whether more data exists. |
| API versioning | Routes use `/api/v1/` prefix strings but no formal versioning library |

**Recommendation:** Formal API versioning remains the next optional starter-template extension if the template needs multi-version route negotiation beyond the current `/api/v1/` prefix convention.

---

## Minor Issues

- ~~**Dockerfile installs SQL Server ODBC tools in production image**~~ — resolved. Removed ODBC tools and `mssql-tools18` from the runtime stage. The API uses `Microsoft.Data.SqlClient` (not ODBC); for `sqlcmd` debugging, use the `db` container directly.
- ~~**CI pipeline skips integration tests**~~ — resolved. A separate `integration` job now runs Testcontainers-based tests after the unit test job passes.
- ~~**CORS is fully permissive in development**~~ — resolved. Added comment clarifying intent: dev is permissive for local frontend testing; production blocks all browser cross-origin by default (secure for API-only use). To allow a browser SPA, configure `AllowedOrigins` in appsettings.
- ~~**`Email.IsValidEmail` uses try/catch for flow control**~~ — resolved. Now uses `MailAddress.TryCreate()` (available since .NET 8) to avoid exception-based flow control.
- ~~**No `appsettings.Development.json`**~~ — resolved. Added with `localhost` connection string defaults for standalone dev without Aspire.
- ~~**`Order.Reconstitute()` is public**~~ — now `internal`, visible only to the test assembly via `InternalsVisibleTo`.
- ~~**Scalar UI replaces Swagger UI**~~ — no longer relevant. Swashbuckle was removed from .NET 9+; Scalar is the standard replacement for OpenAPI UI.
- ~~**`Directory.Build.props` lock file path uses backslashes**~~ — resolved. `NuGetLockFilePath` now uses `/` for explicit cross-platform compatibility.
- ~~**CI pipeline missing NuGet cache**~~ — resolved. `actions/setup-dotnet` now uses built-in NuGet caching keyed from `packages.lock.json`.
- ~~**No Dockerfile health check**~~ — resolved. The runtime image now includes a `HEALTHCHECK` targeting `/health/live`.
- ~~**ServiceDefaults only adds liveness probe**~~ — resolved at the API layer. The API now exposes `/health/ready` backed by a database readiness check, alongside `/health/live` and `/alive`.

---

## Test Coverage Summary

| Category | Files | What's Tested |
|----------|-------|---------------|
| Domain unit tests | 6 | Entity creation, validation, state transitions, value object behavior |
| Property-based (FsCheck) | 5 | Money arithmetic invariants, order state machine, GST calculations, email validation |
| Convention tests | 6 classes | Architecture boundaries, naming, CQRS separation, domain encapsulation, persistence mapping, Dapper SQL quality, DateTimeOffset enforcement, constraint naming enforcement, event routing contract validation, domain third-party dependency isolation |
| Application tests | 9 | All command handlers tested with in-memory DbContext |
| Infrastructure tests | 3 | OutboxMessage mutation tests, OutboxProcessor batch processing with Moq ServiceBusSender, ProblemDetails validation-error customization |
| Integration tests | 4+ | Full API endpoint testing with Testcontainers SQL Server, DbUp migrations, ProblemDetails responses |
| Aspire integration tests | 4 | End-to-end pipeline testing via DistributedApplicationTestingBuilder: health endpoints, CRUD path, stock decrement, outbox-to-Service-Bus eventing verification |
| Test builders | 3 | Fluent builders for Customer, Product, Order |

**Coverage:** Every command handler has targeted tests. All 9 handlers (Create/Update/Delete for Product and Customer, plus CreateOrder, UpdateOrderStatus, CancelOrder) have test classes covering successful operations, not-found exceptions, and domain invariant enforcement.

---

## Verdict

A well-engineered starter template that gets the hard things right: architecture enforcement through convention tests across 6 classes (including Dapper SELECT * prevention via IL inspection), proper CQRS separation with zero violations, rich domain modeling with state machines and value objects, and modern DevOps with Aspire orchestration.

Issues #1–#14, #16–#43, and #46–#55 have all been resolved. Recent hardening addressed critical security and correctness gaps: order creation now sources pricing from the catalog, stock reservation uses atomic SQL to prevent overselling, cancellation restores reserved stock through every exposed cancellation path, the outbox persists events transactionally, rate limiting is enforced globally by verified identity where available, validation failures return structured field errors, domain dependency isolation is convention-guarded, mixed-currency orders are rejected at the domain level, and APIM-projected identity headers now require a signed gateway assertion in production-like mode. The Service Bus integration was hardened with proper resource disposal, retry logic for transient failures, validated configuration, and optimized database indexing. Open work is now concentrated in outbox lock duration during publish, consumer-observable AppHost eventing tests, and keeping order lifecycle changes intent-based instead of generic status assignment.

The convention tests remain the standout feature. They catch categories of architectural drift that code review alone would miss, and they scale as the codebase grows.

**Best suited for:** Teams starting a new .NET API who want architectural guardrails from day one. Authentication validation is left to the API gateway by design, while the API enforces a signed trusted-edge identity contract; the full event pipeline (domain events → outbox → Service Bus → Azure Functions) is implemented for the Order aggregate.
