# Starter App Project with .NET Aspire

A comprehensive tutorial project demonstrating modern .NET development with Docker, SQL Server, and .NET Aspire orchestration.

## 🚀 Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- Visual Studio Code or Visual Studio 2022

### Running with .NET Aspire (Recommended for Development)
```powershell
# Navigate to the AppHost project
cd src\StarterApp.AppHost

# Run the Aspire orchestration
dotnet run
```
- **Aspire Dashboard**: Opens automatically at http://localhost:15061
- **API**: Available at dynamically assigned port (shown in dashboard)

### Running with Docker Compose (Production-like)
```powershell
# From solution root
docker-compose up --build
```
- **API**: http://localhost:8080
- **SQL Server**: localhost:1433

## 🎯 What This Project Demonstrates

1. **Modern .NET Development**
   - .NET 8 Web API with clean architecture
   - Domain-driven design with value objects
   - Repository pattern with Entity Framework Core

2. **Database Management**
   - SQL Server with Entity Framework Core
   - Database migrations with DbUp
   - Connection string management across environments

3. **Containerization**
   - Multi-stage Docker builds
   - Docker Compose orchestration
   - Health checks and container dependencies

4. **Cloud-Native Development**
   - .NET Aspire for local orchestration
   - Service discovery and configuration
   - Built-in observability and telemetry

5. **DevOps & Deployment**
   - Azure deployment with Container Apps
   - PowerShell automation scripts
   - Environment-specific configurations

## 📁 Project Structure

```
starterapp/
├── src/
│   ├── StarterApp.AppHost/          # .NET Aspire orchestration
│   ├── StarterApp.Api/              # Main Web API
│   ├── StarterApp.Domain/           # Domain models and interfaces
│   ├── StarterApp.DbMigrator/       # Database migration console app
│   └── StarterApp.ServiceDefaults/  # Shared Aspire configuration
├── docs/                           # Step-by-step tutorials
├── scripts/                       # Automation scripts
├── docker-compose.yml             # Docker orchestration
└── test-connection.ps1           # Database connection tester
```

## 📚 Step-by-Step Guide

Follow the numbered directories in the `docs/` folder:

1. **[.NET Setup](docs/01-dotnet-setup/README.md)** - Create the Web API project
2. **[SQL Server Setup](docs/02-sql-server-setup/README.md)** - Database configuration and migrations
3. **[Docker Setup](docs/03-docker-setup/README.md)** - Containerization with Docker Compose
4. **[Azure Deployment](docs/04-azure-deployment/README.md)** - Deploy to Azure Container Apps
5. **[Aspire Setup](docs/05-aspire-setup/README.md)** - .NET Aspire orchestration

## 🛠️ Development Commands

```powershell
# Test database connection
.\test-connection.ps1

# Run database migrations
cd src\StarterApp.DbMigrator
dotnet run

# Run API directly
cd src\StarterApp.Api
dotnet run

# Run tests
dotnet test
```

## 🔍 Key Features

- **Clean Architecture**: Separation of concerns with Domain, Application, and Infrastructure layers
- **Health Checks**: Built-in health monitoring for all services
- **Observability**: Distributed tracing, metrics, and structured logging
- **Configuration Management**: Environment-specific settings with .NET configuration
- **Database Migrations**: Automated schema updates with DbUp
- **Container Orchestration**: Both Docker Compose and .NET Aspire support

## 📖 Learning Resources

This project serves as a practical example for learning:
- Modern .NET development practices
- Docker containerization strategies
- Database migration patterns
- Cloud-native application development
- DevOps automation with PowerShell