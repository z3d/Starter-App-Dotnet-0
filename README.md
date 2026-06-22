# Starter App Project with .NET Aspire

A production-grade starter template and reference architecture for modern .NET development with .NET Aspire orchestration, PostgreSQL, Docker-backed local dependencies, and deployable container images.

## 🚀 Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (**Required** - Aspire-managed dependencies, the Functions runtime container, Testcontainers, and image builds use Docker)
- Visual Studio Code or Visual Studio 2026

> **⚠️ Important**: Docker Desktop must be installed and running before executing Aspire orchestration or integration tests. Aspire is the supported local run path; Docker provides the local infrastructure containers underneath it.

### Running with .NET Aspire (Recommended for Development)
```powershell
# Navigate to the AppHost project
cd src\StarterApp.AppHost

# Run the Aspire orchestration
dotnet run
```
- **Aspire Dashboard**: Opens automatically at https://localhost:17113
- **API**: Available at dynamically assigned port (shown in dashboard)
- **Scalar API Reference**: `https://localhost:<api-port>/scalar`

#### Optional: local gateway emulator

By default the locally-orchestrated API runs in `GatewayIdentity:Mode=UnsignedDevelopment` and trusts projected `X-Authenticated-*` identity headers directly. To exercise the production verification path locally, opt into the `StarterApp.Gateway` reverse-proxy emulator, which fronts the API, projects the normalized identity headers, and signs the `X-Gateway-Assertion` the API verifies in `Required` mode:

```powershell
# From src\StarterApp.AppHost
dotnet run -- --gateway          # or set ENABLE_GATEWAY=true
```

With the gateway running, open **`/demo`** on the gateway origin for a self-contained interactive walkthrough: it drives probe → identity → product → customer → order through the signed path with an animated pipeline, same-origin with the proxied API (so its fetches need no CORS). Untick a scope or `mfa` in the identity step to watch the API refuse a write with `403` from the signed assertion alone.

## 🎯 What This Project Demonstrates

1. **Modern .NET Development**
   - .NET 10 Minimal APIs with clean architecture
   - Endpoint filters for cross-cutting concerns
   - Domain-driven design with value objects
   - CQRS pattern with mediator
   - Entity Framework Core for commands, Dapper for queries
   - Distributed caching with Redis for by-id queries (mediator pipeline behavior)

2. **Database Management**
   - PostgreSQL with Entity Framework Core and Npgsql
   - Database migrations with DbUp
   - Connection string management across environments

3. **Container Images and Runtime Dependencies**
   - Multi-stage Dockerfiles for deployable workloads
   - Direct image build validation for API, DbMigrator, and Functions
   - Readiness/liveness health checks for container platforms
   - No separate Docker Compose local stack; Aspire owns local orchestration

4. **Cloud-Native Development**
   - .NET Aspire for local orchestration
   - Service discovery and configuration
   - Built-in observability and telemetry

5. **Asynchronous Event Pipeline**
   - Domain events raised inside aggregates, persisted to outbox atomically
   - BackgroundService polls outbox and publishes to Azure Service Bus
   - Azure Functions subscribe via topic subscriptions with correlation filters; current sample subscribers capture inbound payloads and log trigger activity
   - Service Bus emulator for local development, started by Aspire through Docker

6. **DevOps & CI**
   - GitHub Actions CI pipeline with separate build/unit, Testcontainers integration, Aspire end-to-end, and Docker image build jobs
   - Security & performance workflows: CodeQL static analysis, secret scanning (gitleaks), OWASP ZAP DAST, and nightly k6 performance gates
   - Direct Docker image build validation
   - Smoke test script for validating live deployments

## 📁 Project Structure

```
starterapp/
├── src/
│   ├── StarterApp.AppHost/          # .NET Aspire orchestration
│   ├── StarterApp.AppHost.Tests/    # Aspire integration tests (DistributedApplicationTestingBuilder)
│   ├── StarterApp.Api/              # Main Web API (+ outbox processor)
│   ├── StarterApp.Domain/           # Domain models and interfaces
│   ├── StarterApp.Functions/        # Azure Functions (Service Bus subscribers)
│   ├── StarterApp.Gateway/          # Dev-only APIM gateway emulator (reverse proxy + signed identity projection)
│   ├── StarterApp.DbMigrator/       # Database migration console app
│   ├── StarterApp.ServiceDefaults/  # Shared Aspire configuration
│   └── StarterApp.Tests/            # Unit, convention, integration, fuzzing tests
├── docs/                           # Setup and onboarding guides
└── scripts/                       # Smoke test script
```

## 📚 Step-by-Step Guide

Follow the numbered directories in the `docs/` folder:

1. **[.NET Setup](docs/01-dotnet-setup/README.md)** - Create the Web API project
2. **[PostgreSQL Setup](docs/02-postgres-setup/README.md)** - Database configuration and migrations
3. **[Container Images and Docker Dependencies](docs/03-docker-setup/README.md)** - Dockerfiles, Aspire-managed container dependencies, and image build validation
4. **[Aspire Setup](docs/05-aspire-setup/README.md)** - .NET Aspire orchestration

## 🧪 Running CI Locally with Act

[Act](https://github.com/nektos/act) lets you run GitHub Actions workflows locally using Docker.

### Prerequisites
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (must be running)
- [Act](https://github.com/nektos/act#installation):
  ```powershell
  # macOS
  brew install act

  # Windows (via Chocolatey)
  choco install act-cli

  # Windows (via winget)
  winget install nektos.act
  ```

### Running the CI Workflow
```powershell
# Run the workflow (simulates a push event)
act push

# Run the workflow (simulates a pull request event)
act pull_request

# Use a larger runner image for better compatibility
act push -P ubuntu-latest=catthehacker/ubuntu:full-latest

# Dry run (shows what would execute without running)
act push -n

# Run with verbose output for debugging
act push -v
```

> **Note**: The GitHub workflow has separate unit, Testcontainers integration, Aspire, and Docker image build jobs. When using `act`, run a targeted job if your local Docker setup cannot support the full workflow.

## 🛠️ Development Commands

```powershell
# Run database migrations
cd src\StarterApp.DbMigrator
dotnet run

# Run API directly
cd src\StarterApp.Api
dotnet run

# Run all tests (integration and Aspire tests require Docker Desktop to be running)
dotnet test

# Run only non-integration tests (no Docker required)
dotnet test --filter "FullyQualifiedName!~Integration"
```

## 🔍 Key Features

- **Clean Architecture**: Separation of concerns with Domain, Application, and Infrastructure layers
- **Modern .NET Patterns**: Uses C# 13/.NET 10 features like collection expressions, guard clauses, and using declarations
- **Gateway-Based Authentication**: The API trusts a normalized `X-Authenticated-*` identity contract projected by a front gateway (APIM in production) and verifies a signed `X-Gateway-Assertion` in `Required` mode. `StarterApp.Gateway` is a dev-only reverse-proxy emulator of that gateway, so local orchestration can exercise the signed verification path
- **Health Checks**: Built-in liveness/readiness probes plus durable dependency checks for the database, distributed cache, Service Bus, and payload archive store
- **Distributed Caching**: Redis-backed by-id query caching via mediator pipeline behavior; list queries are intentionally not cached because `IDistributedCache` cannot invalidate by pattern
- **Cache Safety Conventions**: Convention tests enforce non-empty deterministic cache keys, by-id-only caching, and invalidator injection for non-create mutations on cacheable entities
- **Outbox Pattern**: Domain events are captured durably and published to Azure Service Bus via BackgroundService
- **Azure Functions**: Service Bus subscriber samples for email notifications and inventory reservation; they currently archive inbound payloads and log trigger activity
- **Payload Archive / PII Audit**: Bounded HTTP payload capture plus full Service Bus payload archiving to Blob storage with explicit fail-open/fail-closed policy
- **Observability**: Distributed tracing, metrics, and structured logging
- **Configuration Management**: Environment-specific settings with .NET configuration
- **Database Migrations**: Automated schema updates with DbUp
- **Local Orchestration**: .NET Aspire is the supported full-stack local run path
- **Comprehensive Testing**: Unit, convention, integration, property-based (FsCheck), and Aspire end-to-end tests

## Documentation

- **[Blog Site](https://z3d.github.io/blog/)**: GitHub Pages site for long-form engineering notes ([z3d/blog](https://github.com/z3d/blog))
- **[API Endpoints](docs/API-ENDPOINTS.md)**: Complete documentation of all Minimal API endpoints with examples and usage patterns
- **[Architectural Guide](CLAUDE.md)**: Comprehensive guide to the Clean Architecture implementation, patterns, and conventions
- **[Setup Guides](docs/)**: Step-by-step guides for development environment setup and deployment

## Reference Material

This template also serves as a practical reference for:
- Modern .NET development practices
- Container image and deployment validation strategies
- Database migration patterns
- Cloud-native application development
- CI/CD pipeline design with GitHub Actions
