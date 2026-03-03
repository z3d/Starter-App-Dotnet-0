# .NET 10 Clean Architecture Template

Architectural patterns, conventions, and standards for a .NET 10 project using Aspire orchestration. Detailed implementation guides are in `.claude/rules/`.

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

## Architecture Principles

### Core Design Principles

**Domain-Driven Design**
- Rich domain models: entities contain business behavior, not just properties
- Value objects: immutable objects with business meaning (Email, Money)
- Aggregate roots control access and maintain consistency
- Reconstitute pattern for rebuilding aggregates from database rows

**CQRS Implementation**
- Commands → EF Core (ApplicationDbContext) → return DTOs
- Queries → Dapper (IDbConnection) → return ReadModels
- Never mix: no DTOs from queries, no ReadModels from commands
- Custom mediator (not MediatR) with auto-registration via `builder.Services.AddMediator()`

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

## Pre-Commit Checklist

**ALWAYS** complete before committing:
1. `dotnet format` — apply formatting standards
2. `dotnet build` — ensure compilation success
3. `dotnet test` — verify all tests pass
4. `dotnet restore` — update lock files if dependencies changed
5. Ensure `packages.lock.json` files are committed

## Key Patterns

**DDD Entities**: Private setters, protected EF Core constructor, public domain constructor with validation, domain methods for mutations. See `.claude/rules/ddd-implementation.md`.

**CQRS Handlers**: Commands use `ApplicationDbContext` directly (no repository). Queries use `IDbConnection` with Dapper SQL. Convention tests enforce this separation. See `.claude/rules/cqrs-patterns.md`.

**Data Access**: EF Core with `OwnsOne` for value objects, DbUp migrations in DbMigrator project. See `.claude/rules/data-access.md`.

**Testing**: xUnit + FsCheck property-based testing + Best.Conventional architectural conventions (23 tests across `NamingConventionTests`, `CqrsConventionTests`, `DomainConventionTests`). Convention tests use built-in conventions where possible, custom `ConventionSpecification` for structural checks. See `.claude/rules/testing-strategy.md`.

**API Design**: Minimal APIs with `IEndpointDefinition` pattern, auto-discovery, endpoint filters for route-specific logic. See `.claude/rules/api-design.md`.

**Debugging**: Always reproduce with a failing test first, get full stack trace, fix root cause not symptoms. See `.claude/rules/development-workflow.md`.

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
