# Architecture Review

## Overview

A .NET 10 Clean Architecture starter template demonstrating CQRS, DDD, and modern DevOps practices across an e-commerce domain (Products, Customers, Orders) with Aspire orchestration and SQL Server.

**Score: 8/10**

---

## Strengths

### Clean Architecture with Enforced Boundaries

The project separates concerns across well-defined layers:

- **Domain** (`StarterApp.Domain`) — Pure business logic, zero infrastructure dependencies
- **Application** (`StarterApp.Api/Application`) — Commands, queries, validators, DTOs
- **Infrastructure** (`StarterApp.Api/Infrastructure`) — Mediator, validation, data access
- **API** (`StarterApp.Api/Endpoints`) — Minimal APIs with endpoint definitions

These boundaries are **enforced by 37 convention tests** across 5 test classes, not just documented. For example, `CqrsConventionTests` ensures command handlers never touch `IDbConnection` and query handlers never touch `DbContext`. `ApiConventionTests` ensures endpoints only dispatch through the mediator and that validators remain pure. `PersistenceConventionTests` ensures every entity has a `DbSet` registration and value objects use `OwnsOne` instead.

### CQRS Implementation

The read/write split is well executed:

- **Commands** flow through EF Core `DbContext` for writes
- **Queries** flow through Dapper `IDbConnection` for reads
- A **custom mediator** replaces MediatR, avoiding commercial licensing while maintaining the same dispatch pattern with auto-discovery and integrated validation

### Rich Domain Models

Entities are not anemic:

- `Order` has a proper state machine (Pending > Confirmed > Processing > Shipped > Delivered, with cancellation)
- Value objects (`Money`, `Email`) are immutable with static factories and equality overrides
- Private setters and protected constructors enforce encapsulation
- `Reconstitute()` factory methods handle database hydration without bypassing invariants at creation time

### Convention Tests

The standout feature. 37 tests using Best.Conventional across 5 classes enforce:

**Naming** (`NamingConventionTests` — 9 tests)
- Endpoints, DTOs, commands, queries, handlers, validators, services, and test classes follow naming conventions

**CQRS Boundaries** (`CqrsConventionTests` — 5 tests)
- Command handlers must not depend on `IDbConnection`
- Query handlers must not depend on `DbContext`
- Every command and query must have a handler
- Commands must implement both `ICommand` and `IRequest<T>`
- Queries must implement both `IQuery<T>` and `IRequest<T>`

**Domain Integrity** (`DomainConventionTests` — 7 tests)
- Entities have private property setters and non-public default constructors
- Value objects are immutable and override `Equals`/`GetHashCode`
- DTOs have public getters
- Async methods have `Async` suffix; no async void; no `DateTime.Now`

**API Layer** (`ApiConventionTests` — 8 tests)
- Endpoints must not depend on `DbContext` or `IDbConnection` directly
- Validators must be pure (no database access)
- DTOs must not have instance methods (plain data carriers only)
- Mappers must be static classes
- Handlers must not depend on `IMediator` (no handler-to-handler dispatch chains)
- Domain types must not reference the API assembly

**Persistence** (`PersistenceConventionTests` — 6 tests — _new_)
- All domain entities must have a `DbSet<T>` in `ApplicationDbContext`
- Value objects must not be registered as `DbSet` (use `OwnsOne`)
- Entities with domain enums must have enum properties configured
- `DbContext` must not have static mutable state
- Collection navigation properties must not have public setters
- Migration scripts must follow numbered prefix pattern (`0001_`)

### Property-Based Testing

FsCheck tests go beyond typical unit testing:

- Money arithmetic (commutativity, associativity, currency validation)
- Order state machine (valid/invalid transitions)
- OrderItem GST calculations and boundary conditions
- Email format validation edge cases

### DevOps and Observability

- Aspire orchestration — single command to spin up API + SQL Server + Seq
- Serilog structured logging with console, file, Seq, and OpenTelemetry sinks
- Docker multi-stage build with docker-compose
- CI pipeline with GitHub Actions
- Health checks at `/health`

### Build Configuration

`Directory.Build.props` enforces quality globally:

- Warnings as errors
- Nullable reference types
- Deterministic builds
- Package lock files for reproducibility

---

## Weaknesses

### 1. No Authentication or Authorization

**Severity: High**

No auth exists. Rate limiting and security headers are present, but they protect an open API. For a starter template, this is the most common thing teams need to add first.

**Recommendation:** Add a JWT bearer setup with a placeholder identity provider. Even a minimal `AddAuthentication().AddJwtBearer()` with `RequireAuthorization()` on the endpoint groups would demonstrate the pattern.

### 2. Thin Application Layer

**Severity: Medium**

Command handlers are mostly CRUD pass-through. The mediator + validator pipeline is well-built infrastructure, but the handlers don't demonstrate complex business workflows (e.g., "create order > validate stock > reserve inventory > send confirmation").

**Recommendation:** Add a `CreateOrderCommandHandler` that checks product stock before creating the order, demonstrating cross-aggregate coordination through the application layer.

### 3. No Repository Abstraction

**Severity: Low**

`DbContext` is used directly in command handlers. This is a valid simplification, but it makes unit testing handlers harder without Testcontainers.

**Recommendation:** This is a deliberate trade-off. If unit test speed becomes an issue, introduce a lightweight `IRepository<T>` only then.

### 4. Database Migrations Run on API Startup

**Severity: Medium**

`DatabaseMigrator.MigrateDatabase()` runs in `Program.cs` on startup. With multiple replicas, this creates race conditions. The standalone `DbMigrator` project exists but isn't clearly the designated production path.

**Recommendation:** Document that production deployments should run `DbMigrator` as a job/init container, and gate the API startup migration behind a configuration flag (e.g., `RunMigrationsOnStartup=true` defaulting to `false` in production).

### 5. Missing Patterns for a Starter Template

**Severity: Low**

Several common patterns are absent:

| Pattern | Impact |
|---------|--------|
| Domain events | No way to react to domain changes (e.g., "order created" > send email) |
| Outbox pattern | No reliable event publishing |
| Caching | No `IDistributedCache` or response caching |
| Pagination response wrapper | Endpoints accept `page`/`pageSize` but return raw collections |
| API versioning middleware | Routes use `/api/v1/` prefix strings but no formal versioning library |

**Recommendation:** Domain events are the highest-value addition. The custom mediator already has the infrastructure to dispatch them.

### 6. Test Coverage Gaps

**Severity: Medium**

Convention and fuzzing tests are strong, but application-layer handler tests (the actual business logic) appear sparse relative to the infrastructure tests. No load/performance testing baseline exists.

**Recommendation:** Add handler-level tests that verify command handlers interact correctly with `DbContext` (using Testcontainers). This would complement the existing convention tests that verify structural rules.

---

## Minor Issues

- **CORS** is fully permissive in development (`AllowAnyOrigin`) — standard but worth a code comment
- **`Reconstitute` on `Order`** bypasses validation, which is correct for DB loading but could be misused — consider making it `internal`
- **Scalar UI** replaces Swagger, which may slow onboarding for developers expecting Swagger UI
- **No `PagedResult<T>`** wrapper — endpoints return raw `IEnumerable<T>` without total count or pagination metadata

---

## Verdict

A well-engineered, opinionated starter template that gets the hard things right: architecture enforcement through tests, proper CQRS separation, rich domain modeling, and modern DevOps. The convention tests alone make it worth studying.

Its main gap is that it's more of an architecture showcase than a production-ready starter. Adding authentication, domain events, and a non-trivial business workflow would close that gap.

**Best suited for:** Teams starting a new .NET API who want guardrails from day one and are willing to add auth/caching/eventing themselves.
