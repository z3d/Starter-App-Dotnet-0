# Step 1: Setting Up the .NET 8 Web API Project

## Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Visual Studio Code or Visual Studio 2022
- Git (for version control)

## Project Overview

This project uses a clean architecture approach with the following structure:
- **DockerLearningApi** - Web API presentation layer
- **DockerLearning.Domain** - Core business logic and entities
- **DockerLearning.DbMigrator** - Database migration console application
- **DockerLearning.AppHost** - .NET Aspire orchestration (added in Step 5)

## Current Project Status

✅ **Already Created!** - The .NET projects have been set up with:

### 1. Domain Layer (`DockerLearning.Domain`)
Contains core business entities and interfaces:

```csharp
// Product entity with rich domain model
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Money Price { get; set; } = new();
    public int Stock { get; set; }
    public DateTime LastUpdated { get; set; }
}

// Value object for money handling
public record Money(decimal Amount, string Currency = "USD");
```

### 2. Web API (`DockerLearningApi`)
Features implemented:
- RESTful API with full CRUD operations
- Entity Framework Core with SQL Server
- Repository pattern implementation
- Health checks for monitoring
- Swagger/OpenAPI documentation
- Structured logging with Serilog
- Environment-specific configuration

### 3. Database Migrations (`DockerLearning.DbMigrator`)
- Console application for running database migrations
- DbUp library for version-controlled schema changes
- Embedded SQL script resources
- Connection string configuration

## Running the API Locally

### Option 1: Run API Directly
```powershell
# Navigate to the API project
cd c:\dev\scratchpad\dockerlearning\src\DockerLearningApi

# Run the project
dotnet run
```

**Access points:**
- API: https://localhost:7286 or http://localhost:5164
- Swagger UI: https://localhost:7286/swagger
- Health Check: https://localhost:7286/health

### Option 2: Run with .NET Aspire (Recommended)
```powershell
# Navigate to the AppHost project
cd c:\dev\scratchpad\dockerlearning\src\DockerLearning.AppHost

# Run with Aspire orchestration
dotnet run
```

This will start:
- SQL Server container
- API with automatic service discovery
- Aspire dashboard with observability

## Testing the API

### Manual Testing
1. Open the Swagger UI at the API URL
2. Try the GET `/api/products` endpoint
3. Use POST to create new products
4. Test PUT and DELETE operations

### Automated Testing
```powershell
# Run unit tests
cd c:\dev\scratchpad\dockerlearning
dotnet test
```

The test suite includes:
- Unit tests for domain models
- Integration tests for API endpoints
- Repository pattern tests
- Value object validation tests

## Key Features Implemented

### 🏗️ Clean Architecture
- **Domain**: Core business logic isolated from infrastructure
- **Application**: Use cases and application services
- **Infrastructure**: Data access and external dependencies
- **Presentation**: Web API controllers and DTOs

### 🔧 Configuration Management
- Environment-specific `appsettings.json` files
- Connection string management
- Docker-specific configuration support

### 📊 Observability
- Health checks for application and database
- Structured logging with Serilog
- Request/response logging
- Error handling and logging

### 🛡️ Best Practices
- Repository pattern for data access
- Value objects for domain modeling
- Dependency injection container
- Async/await patterns throughout

## Next Steps

The foundation is complete! Continue to:

**Next Step:** [Step 2: SQL Server Setup](../02-sql-server-setup/README.md) to understand the database configuration and migration strategy.