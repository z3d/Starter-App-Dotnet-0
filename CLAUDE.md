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
│   │   └── Infrastructure/             # Cross-cutting concerns
│   ├── [ProjectName].Domain/           # Domain Layer (Core Business Logic)
│   ├── [ProjectName].AppHost/          # Aspire Orchestration Host
│   ├── [ProjectName].ServiceDefaults/  # Shared Aspire Service Configuration
│   ├── [ProjectName].DbMigrator/       # Database Migration Tool
│   └── [ProjectName].Tests/            # Comprehensive Test Suite
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

**CQRS Implementation**
- Commands → EF Core (ApplicationDbContext) → return DTOs
- Queries → Dapper (IDbConnection) → return ReadModels
- Never mix: no DTOs from queries, no ReadModels from commands
- Custom mediator (not MediatR) with auto-registration via `builder.Services.AddMediator()`

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

**Data Access**: EF Core with `OwnsOne` for value objects, DbUp migrations in DbMigrator project. See `.claude/skills/data-access/SKILL.md`.

**Domain Events / Outbox**: Domain events are raised inside aggregates and persisted to the `OutboxMessages` table by `ApplicationDbContext` during `SaveChangesAsync`. Handlers still call a single save; the DbContext handles durable event capture internally so external publishers can process the outbox asynchronously later.

**Database Migrations**: Migrations run exclusively via the dedicated `DbMigrator` console app — never embedded in API startup (eliminates race conditions with multiple replicas).
- **Aspire:** `AppHost` runs `DbMigrator` with `WaitFor` dependency on SQL Server
- **Docker Compose:** `migrator` service runs before the API (`condition: service_completed_successfully`)
- **Standalone dev:** Run `dotnet run --project src/StarterApp.DbMigrator` before starting the API
- **Integration tests:** `TestFixture.RunDbUpMigrations()` runs migrations independently

**Testing**: xUnit + FsCheck property-based testing + Best.Conventional architectural conventions across 6 test classes (including `DapperConventionTests` for SELECT * prevention via IL inspection). Convention tests use built-in conventions where possible, custom `ConventionSpecification` for structural checks. See `.claude/skills/testing-strategy/SKILL.md`.

**API Design**: Minimal APIs with `IEndpointDefinition` pattern, auto-discovery, endpoint filters for route-specific logic. See `.claude/skills/api-design/SKILL.md`.

**Health Endpoints**: Expose `/health` for aggregate status, `/health/ready` for readiness (including database connectivity), and `/health/live` plus `/alive` for liveness. Docker and container platforms should use readiness for traffic gating and liveness for restart decisions.

**Pagination**: List endpoints use `page`/`pageSize` query params with SQL `OFFSET/FETCH`. Handlers fetch `pageSize + 1` rows; endpoints trim the extra row and return a `PagedResponse<T>` envelope (`{ data: [...], hasMore: true/false }`). This avoids expensive COUNT queries. Total count is a UI concern — if a frontend needs it, add a separate count endpoint rather than embedding it in every list response.

**Debugging**: Always reproduce with a failing test first, get full stack trace, fix root cause not symptoms. See `.claude/skills/development-workflow/SKILL.md`.

## Documentation Maintenance

- Update CLAUDE.md with every architectural change
- Document WHY not WHAT
- Keep subsidiary docs in sync: `README.md`, `ASPIRE_SETUP_COMPLETE.md`, `docs/API-ENDPOINTS.md`, `docs/01-dotnet-setup/`, `docs/03-docker-setup/`, `docs/04-azure-deployment/`, `docs/05-aspire-setup/`

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
