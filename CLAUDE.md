# .NET 10 Clean Architecture Template

Architectural patterns, conventions, and standards for a .NET 10 project using Aspire orchestration. Detailed implementation guides are in `.claude/skills/`.

## Project Overview

**Clean Architecture** .NET 10 solution implementing:
- **Minimal APIs**: Endpoint-based architecture with `IEndpointDefinition` auto-discovery
- **CQRS Pattern**: Separate command/query responsibilities
- **Domain-Driven Design**: Rich domain models with business logic
- **Aspire Orchestration**: Service orchestration and observability
- **Reproducible Builds**: Package lock files and centralized configuration

### Solution Structure

```
Solution Root/
├── src/
│   ├── [ProjectName].Api/              # Minimal API Layer
│   │   ├── Endpoints/                  # API endpoint definitions
│   │   │   ├── CustomerEndpoints.cs
│   │   │   ├── OrderEndpoints.cs
│   │   │   ├── ProductEndpoints.cs
│   │   │   ├── IEndpointDefinition.cs
│   │   │   ├── EndpointExtensions.cs
│   │   │   └── Filters/
│   │   ├── Application/                # CQRS commands and queries
│   │   ├── Data/                       # EF Core DbContext
│   │   └── Infrastructure/             # Cross-cutting concerns (outbox, mediator)
│   ├── [ProjectName].Domain/           # Domain Layer (Core Business Logic)
│   ├── [ProjectName].Functions/        # Azure Functions (Service Bus subscribers)
│   ├── [ProjectName].AppHost/          # Aspire Orchestration Host
│   ├── [ProjectName].AppHost.Tests/    # Aspire Integration Tests (DistributedApplicationTestingBuilder)
│   ├── [ProjectName].ServiceDefaults/  # Shared Aspire Service Configuration
│   ├── [ProjectName].DbMigrator/       # Database Migration Tool
│   └── [ProjectName].Tests/            # Unit, Convention, Integration, Fuzzing Tests
├── config/                             # Emulator configuration (Service Bus topology)
└── docs/                               # Documentation
    └── API-ENDPOINTS.md
```

## AI-Agent Maintenance Context

This codebase is maintained by AI agents. Design decisions favour **mechanical rules over architectural taste**:
- Convention tests enforce structural rules that agents follow perfectly — ambiguity is the real risk, not boilerplate
- Every command and query must have a validator (enforced by convention test), even trivial ones — agents generate boilerplate cheaply, and skipping coverage creates judgment calls that cause drift
- **Example:** The validator coverage rule was a deliberate trade-off. For human-maintained code, requiring a validator for `DeleteProductCommand` (just `Id > 0`) would be busywork. For agent-maintained code, the mechanical rule eliminates the question "does this command need a validator?" and the convention test catches missing validators on every build.

### Validator–Domain Guard Sync Rule

Validators and domain guards intentionally overlap (defense-in-depth). When modifying either:
- **Adding/changing a domain constructor guard or value object validation** → update the corresponding `IValidator<T>` to match
- **Adding/changing a validator rule** → verify the domain guard covers the same invariant as a safety net
- Domain guards throw single exceptions (last line of defense). Validators yield structured multi-error `ValidationError` collections (API UX).

## Architecture Principles

### Core Design Principles

**Domain-Driven Design**
- Rich domain models: entities contain business behavior, not just properties
- Value objects: immutable objects with business meaning (Email, Money)
- Aggregate roots control access and maintain consistency
- Aggregate roots may raise domain events; persistence captures them into the outbox in the same unit of work
- Aggregate roots own child collections via backing fields; EF Core populates via `.Include()`
- `Reconstitute` is internal/test-only — production handlers use tracked EF entities
- **Aggregate ID convention**: aggregates that raise creation events (override `RecordCreation()`) MUST use client-generated `Guid.CreateVersion7()` IDs, assigned in the constructor. Aggregates without creation events MAY use int IDENTITY. Reason: `ApplicationDbContext.SaveChangesWithOutboxAsync` is a single `SaveChanges` — creation events are captured into the outbox BEFORE the save, so Ids must be known client-side. This keeps `EnableRetryOnFailure` safe: no user transaction, retry is transparent. A convention test (`AggregatesOverridingRecordCreation_MustHaveGuidId`) enforces the rule.

**CQRS Implementation**
- Commands → EF Core (ApplicationDbContext) → return DTOs
- Queries → Dapper (IDbConnection) → return ReadModels
- Never mix: no DTOs from queries, no ReadModels from commands
- Custom mediator (not MediatR) with auto-registration via `builder.Services.AddMediator()`
- Pipeline behaviors wrap handler invocation (currently: `CachingBehavior` for `ICacheable` queries)

**Distributed Caching**
- Redis via Aspire `AddRedis` / `AddRedisDistributedCache("redis")` — falls back to in-memory cache when Redis connection string is absent (tests, standalone dev)
- Queries opt in by implementing `ICacheable` (provides `CacheKey` and `CacheDuration`)
- `CachingBehavior` in the mediator pipeline checks cache before handler, stores on miss, skips null results
- Command handlers invalidate specific entity keys via `ICacheInvalidator` after `SaveChangesAsync`
- Only by-id queries are cached — list/collection queries are NOT cached because `IDistributedCache` has no pattern-based deletion, and stale list pages after writes are user-visible bugs. If list caching is needed later, use a versioned namespace approach.
- Convention tests enforce: non-empty cache keys, positive durations, deterministic keys

**Authentication**
- This API assumes it runs behind an API gateway that handles auth — do not add authentication middleware to the API itself
- Rate limiting and security headers provide defense-in-depth at the service level
- If the API is ever exposed directly to clients without a gateway, add `AddAuthentication().AddJwtBearer()` with `RequireAuthorization()` on endpoint groups

**Clean Architecture**
- Domain layer has no external dependencies
- Interface segregation: small, focused interfaces
- Explicit over magic: prefer explicit code over convention-based patterns

### Prohibited Anti-Patterns

**NEVER use these libraries:**
- AutoMapper — use explicit mapping code
- MediatR — use custom mediator (commercial licensing)
- Commercial libraries without explicit stakeholder approval

**NEVER use these patterns:**
- Anemic domain models — domain objects must have behavior
- Mixed CQRS concerns — keep commands and queries strictly separate
- Repository pattern — use DbContext directly in command handlers
- Explicit transactions — use EF Core navigation properties and single `SaveChangesAsync` instead. If you need two saves, the aggregate boundary is wrong.
- `AsNoTracking` + `Update` in command handlers — marks all columns modified, creates lost-update risks. Load tracked entities instead.
- Public `SetId()` methods on entities — EF Core sets Id via private setter. No identity mutation methods.
- Code regions, historical comments, XML doc comments (for app code)
- Dual representation (separate entity/value object pairs)

**Code quality:**
- No tight coupling — use DI and interfaces
- No missing validation — validate at domain and API boundaries
- Follow established naming conventions strictly
- Prefer .NET native libraries over third-party dependencies

## Build Configuration

### Centralized Configuration (Directory.Build.props)
- Target: .NET 10.0, nullable reference types, implicit usings
- Treat warnings as errors, global analyzers enabled
- Package lock files for reproducible builds (`RestorePackagesWithLockFile=true`)

### Central Package Management (Directory.Packages.props)
All NuGet package versions are pinned in `Directory.Packages.props` at the repo root. Individual `.csproj` files contain `<PackageReference Include="X" />` with **no `Version=` attribute** — the version comes from the central file.

**When adding a new package:**
1. Add `<PackageVersion Include="X" Version="Y" />` to `Directory.Packages.props`
2. Add `<PackageReference Include="X" />` (no version) to the consuming `.csproj`
3. Run `dotnet restore --force-evaluate` to update lock files

Never put `Version=` on a `<PackageReference>` — CPM will error on the downgrade/mismatch. Never duplicate a `<PackageVersion>` across multiple entries for the same package. The analyzer `<GlobalPackageReference>` also lives in `Directory.Packages.props` (CPM requires it there, not in `Directory.Build.props`).

### Code Formatting (.editorconfig)
- 180 char line length, file-scoped namespaces, system usings first
- StyleCop rules: SA1200, SA1209, SA1210, SA1211
- Prefer `GlobalUsings.cs` per project over per-file using directives

## Commit Discipline

**ALWAYS commit or ask to commit after completing a task.** Do not leave uncommitted changes in the working tree. If unsure whether the user wants a commit, ask. Uncommitted work is invisible to other agents and at risk of being lost.

## Pre-Commit Checklist

**ALWAYS** complete before committing:
1. `dotnet format` — apply formatting standards
2. `dotnet build` — ensure compilation success
3. `dotnet test` — verify all tests pass
4. `dotnet restore` — update lock files if dependencies changed
5. Ensure `packages.lock.json` files are committed

## Key Patterns

**DDD Entities**: Private setters, protected EF Core constructor, public domain constructor with validation, domain methods for mutations. See `.claude/skills/ddd-implementation/SKILL.md`.

**CQRS Handlers**: Commands load tracked entities via DbContext with `.Include()`, mutate through domain methods, single `SaveChangesAsync(cancellationToken)`. Queries use `IDbConnection` with Dapper SQL. Convention tests enforce this separation. See `.claude/skills/cqrs-patterns/SKILL.md`.

**Data Access**: EF Core with `OwnsOne` for value objects, per-entity `IEntityTypeConfiguration<T>` classes under `Data/Configurations/`, and DbUp migrations in DbMigrator project. `ApplicationDbContext` uses `ApplyConfigurationsFromAssembly()` so entity mapping remains mechanically discoverable and consistency-testable. See `.claude/skills/data-access/SKILL.md`.

**Domain Events / Outbox / Service Bus**: Domain events are raised inside aggregates and persisted to the `OutboxMessages` table by `ApplicationDbContext` during a single `SaveChangesAsync`. Creation events use the `AggregateRoot.RecordCreation()` override pattern — called BEFORE `SaveChanges` because aggregates that raise creation events carry client-assigned `Guid.CreateVersion7()` Ids. Single-SaveChanges means no user transaction is required, which keeps `EnableRetryOnFailure` safe for Azure SQL transient faults. The `OutboxProcessor` BackgroundService polls unprocessed messages with row-level locking (`UPDLOCK, READPAST, ROWLOCK`) and publishes them to Azure Service Bus (topic: `domain-events`) with an `EventType` application property. Azure Functions subscribe via topic subscriptions with correlation filters (`email-notifications`, `inventory-reservation`). The processor marks messages as processed or errored — errored messages are skipped on subsequent polls. Service Bus registration is conditional: no-op when `ConnectionStrings:servicebus` is absent (tests run without Service Bus). The Service Bus emulator runs in Docker for both Aspire (`RunAsEmulator`) and Docker Compose environments. Service Bus duplicate detection is enabled (10-minute window). See `config/servicebus-emulator.json` for topology.

**Event Contracts**: Each `IDomainEvent` implementation exposes a stable, versioned `EventType` property (e.g. `order.created.v1`) via a `const Contract` field. `OutboxMessage.Create` persists `domainEvent.EventType` — **not** the CLR type name. This decouples event routing from class names: renaming a C# class does not break Service Bus subscriptions or existing outbox rows. Convention tests enforce: every event has a non-empty contract, contracts are unique, and Service Bus subscription filters reference valid contracts. When adding a new domain event, give it a `const Contract` and implement `EventType => Contract`.

**Database Migrations**: Migrations run exclusively via the dedicated `DbMigrator` console app — never embedded in API startup (eliminates race conditions with multiple replicas).
- **Aspire:** `AppHost` runs `DbMigrator` with `WaitFor` dependency on SQL Server
- **Docker Compose:** `migrator` service runs before the API (`condition: service_completed_successfully`)
- **Standalone dev:** Run `dotnet run --project src/StarterApp.DbMigrator` before starting the API
- **Integration tests:** `TestFixture.RunDbUpMigrations()` runs migrations independently
- **Constraint naming**: Every constraint must have an explicit name — no anonymous/system-generated names. Convention: `PK_Table`, `FK_Table_Column`, `DF_Table_Column`, `CK_Table_Description`, `IX_Table_Column`. This makes future migrations deterministic (`DROP CONSTRAINT PK_Orders`) instead of requiring dynamic SQL lookups against `sys.default_constraints`. Enforced by convention test from script 0012 onward.

**Testing**: Two test projects:
- `StarterApp.Tests` — xUnit + FsCheck property-based testing + Best.Conventional conventions (including `CachingConventionTests`, `DapperConventionTests` for SELECT * prevention via IL inspection, `DateTimeOffset` enforcement, constraint naming enforcement). Uses WebApplicationFactory + Testcontainers for integration tests. See `.claude/skills/testing-strategy/SKILL.md`.
- `StarterApp.AppHost.Tests` — Aspire integration tests using `DistributedApplicationTestingBuilder`. Spins up the full distributed app (SQL Server, Service Bus emulator, API, Functions) to test the end-to-end pipeline. Tag slow tests with `[Trait("Category", "Aspire")]`. See `.claude/skills/testing-strategy/SKILL.md`.

**Consistency Measurement**: `StarterApp.Tests/Consistency/` is advisory, not a build policy surface. It measures three extensible cohorts against pinned exemplars in `docs/exemplars/`: command handlers, query handlers, and EF configurations. Use it to detect structural drift and surprising outliers; put deterministic rules in convention tests.

**API Design**: Minimal APIs with `IEndpointDefinition` pattern, auto-discovery, endpoint filters for route-specific logic. See `.claude/skills/api-design/SKILL.md`.

**Health Endpoints**: Expose `/health` for aggregate status, `/health/ready` for readiness (including database connectivity), and `/health/live` plus `/alive` for liveness. Docker and container platforms should use readiness for traffic gating and liveness for restart decisions.

**Pagination**: List endpoints use `page`/`pageSize` query params with SQL `OFFSET/FETCH`. Handlers fetch `pageSize + 1` rows; endpoints trim the extra row and return a `PagedResponse<T>` envelope (`{ data: [...], hasMore: true/false }`). This avoids expensive COUNT queries. Total count is a UI concern — if a frontend needs it, add a separate count endpoint rather than embedding it in every list response.

**Debugging**: Always reproduce with a failing test first, get full stack trace, fix root cause not symptoms. See `.claude/skills/development-workflow/SKILL.md`.

## Documentation Maintenance

- Update CLAUDE.md with every architectural change
- Document WHY not WHAT
- Keep subsidiary docs in sync: `README.md`, `ASPIRE_SETUP_COMPLETE.md`, `docs/API-ENDPOINTS.md`, `docs/01-dotnet-setup/`, `docs/03-docker-setup/`, `docs/05-aspire-setup/`
- **Architecture review (`docs/ARCHITECTURE_REVIEW.md`)**: Read this before starting any review or hardening task — it contains prior findings and current score. After fixing issues, update the doc: mark findings as resolved, add any new findings to the "Open Findings" section, and adjust the score. This is the shared artifact that keeps multiple agent conversations in sync.

## Development Commands

```bash
dotnet format                                           # Format code
dotnet build                                            # Build solution
dotnet test                                             # Run all tests
dotnet restore --use-lock-file                          # Update lock files
dotnet restore --locked-mode                            # CI/CD locked restore
dotnet run --project src/StarterApp.AppHost -- --devtunnel  # Run with dev tunnel
act                                                     # Run CI locally (flags in .actrc)
```
