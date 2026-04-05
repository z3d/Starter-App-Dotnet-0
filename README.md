# Starter App Project with .NET Aspire

A comprehensive tutorial project demonstrating modern .NET development with Docker, SQL Server, and .NET Aspire orchestration.

## 🚀 Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (**Required** - must be running for integration tests)
- Visual Studio Code or Visual Studio 2022

> **⚠️ Important**: Docker Desktop must be installed and running before executing integration tests or using Docker Compose. The integration tests use Testcontainers to spin up SQL Server instances.

### Running with .NET Aspire (Recommended for Development)
```powershell
# Navigate to the AppHost project
cd src\StarterApp.AppHost

# Run the Aspire orchestration
dotnet run
```
- **Aspire Dashboard**: Opens automatically at https://localhost:17113
- **API**: Available at dynamically assigned port (shown in dashboard)
- **Scalar API Reference**: `https://localhost:<api-port>/scalar/v1`

### Running with Docker Compose (Production-like)
```powershell
# From solution root
docker-compose up --build
```
- **API**: http://localhost:8080
- **Scalar API Reference**: http://localhost:8080/scalar/v1
- **SQL Server**: localhost:1433

## 🎯 What This Project Demonstrates

1. **Modern .NET Development**
   - .NET 10 Minimal APIs with clean architecture
   - Endpoint filters for cross-cutting concerns
   - Domain-driven design with value objects
   - CQRS pattern with mediator
   - Entity Framework Core for commands, Dapper for queries
   - Distributed caching with Redis (mediator pipeline behavior)

2. **Database Management**
   - SQL Server with Entity Framework Core
   - Database migrations with DbUp
   - Connection string management across environments

3. **Containerization**
   - Multi-stage Docker builds
   - Docker Compose orchestration
   - Readiness/liveness health checks and container dependencies

4. **Cloud-Native Development**
   - .NET Aspire for local orchestration
   - Service discovery and configuration
   - Built-in observability and telemetry

5. **Asynchronous Event Pipeline**
   - Domain events raised inside aggregates, persisted to outbox atomically
   - BackgroundService polls outbox and publishes to Azure Service Bus
   - Azure Functions subscribe via topic subscriptions with correlation filters
   - Service Bus emulator for local development (Docker + Aspire)

6. **DevOps & Deployment**
   - Azure deployment with Container Apps
   - PowerShell automation scripts
   - Environment-specific configurations

## 📁 Project Structure

```
starterapp/
├── src/
│   ├── StarterApp.AppHost/          # .NET Aspire orchestration
│   ├── StarterApp.AppHost.Tests/    # Aspire integration tests (DistributedApplicationTestingBuilder)
│   ├── StarterApp.Api/              # Main Web API (+ outbox processor)
│   ├── StarterApp.Domain/           # Domain models and interfaces
│   ├── StarterApp.Functions/        # Azure Functions (Service Bus subscribers)
│   ├── StarterApp.DbMigrator/       # Database migration console app
│   ├── StarterApp.ServiceDefaults/  # Shared Aspire configuration
│   └── StarterApp.Tests/            # Unit, convention, integration, fuzzing tests
├── config/                         # Emulator configuration (Service Bus topology)
├── docs/                           # Step-by-step tutorials
├── scripts/                       # Automation scripts
└── docker-compose.yml             # Docker orchestration
```

## 📚 Step-by-Step Guide

Follow the numbered directories in the `docs/` folder:

1. **[.NET Setup](docs/01-dotnet-setup/README.md)** - Create the Web API project
2. **[SQL Server Setup](docs/02-sql-server-setup/README.md)** - Database configuration and migrations
3. **[Docker Setup](docs/03-docker-setup/README.md)** - Containerization with Docker Compose
4. **[Azure Deployment](docs/04-azure-deployment/README.md)** - Deploy to Azure Container Apps
5. **[Aspire Setup](docs/05-aspire-setup/README.md)** - .NET Aspire orchestration

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

> **Note**: The CI workflow filters out integration tests, so Docker-in-Docker is not required. If you encounter .NET SDK issues, use the larger runner image (`catthehacker/ubuntu:full-latest`) which includes more pre-installed tools.

## 🛠️ Development Commands

```powershell
# Run database migrations
cd src\StarterApp.DbMigrator
dotnet run

# Run API directly
cd src\StarterApp.Api
dotnet run

# Run tests (requires Docker Desktop to be running)
dotnet test

# Run only unit tests (no Docker required)
dotnet test --filter "FullyQualifiedName!~Integration"
```

## 🔍 Key Features

- **Clean Architecture**: Separation of concerns with Domain, Application, and Infrastructure layers
- **Modern .NET Patterns**: Uses C# 13/.NET 10 features like collection expressions, guard clauses, and using declarations
- **Health Checks**: Built-in health monitoring for all services
- **Distributed Caching**: Redis-backed query caching via mediator pipeline behavior with automatic cache invalidation on writes
- **Outbox Pattern**: Domain events are captured durably and published to Azure Service Bus via BackgroundService
- **Azure Functions**: Service Bus subscribers for email notifications and inventory reservation
- **Observability**: Distributed tracing, metrics, and structured logging
- **Configuration Management**: Environment-specific settings with .NET configuration
- **Database Migrations**: Automated schema updates with DbUp
- **Container Orchestration**: Both Docker Compose and .NET Aspire support
- **Comprehensive Testing**: Unit, convention (7 classes), integration, property-based (FsCheck), and Aspire end-to-end tests

## � Documentation

- **[API Endpoints](docs/API-ENDPOINTS.md)**: Complete documentation of all Minimal API endpoints with examples and usage patterns
- **[Architectural Guide](CLAUDE.md)**: Comprehensive guide to the Clean Architecture implementation, patterns, and conventions
- **[Setup Guides](docs/)**: Step-by-step guides for development environment setup and deployment

## �📖 Learning Resources

This project serves as a practical example for learning:
- Modern .NET development practices
- Docker containerization strategies
- Database migration patterns
- Cloud-native application development
- DevOps automation with PowerShell
