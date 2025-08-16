# .NET Project Architecture Template

This document outlines the architectural patterns, conventions, and technical approach for creating a .NET 9 project template using **Aspire** (not Docker) orchestration. This serves as comprehensive instructions for LLMs creating similar projects.

## Project Overview

This is a **Clean Architecture** .NET 9 solution implementing **CQRS** with **Domain-Driven Design** principles, using **Aspire** for orchestration and observability.

### Solution Structure

```
Solution Root/
├── src/
│   ├── [ProjectName].Api/              # Web API Layer
│   ├── [ProjectName].Domain/           # Domain Layer (Core Business Logic)
│   ├── [ProjectName].AppHost/          # Aspire Orchestration Host
│   ├── [ProjectName].ServiceDefaults/  # Shared Aspire Service Configuration
│   ├── [ProjectName].DbMigrator/       # Database Migration Tool
│   └── [ProjectName].Tests/            # Comprehensive Test Suite
└── docs/                               # Documentation
```

## Key Technical Principles

### Language Features & Conventions

#### Use Global Usings
- **MUST** implement global usings in `GlobalUsings.cs` for each project
- Domain layer example:
  ```csharp
  global using System;
  global using System.Collections.Generic;
  global using System.Linq;
  global using System.Threading;
  global using System.Threading.Tasks;
  global using Serilog;
  global using StarterApp.Domain.Entities;
  global using StarterApp.Domain.ValueObjects;
  ```

#### Project Configuration Standards
- **Target Framework**: `net9.0`
- **Nullable Reference Types**: `<Nullable>enable</Nullable>`
- **Implicit Usings**: `<ImplicitUsings>enable</ImplicitUsings>`
- **Warnings as Errors**: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`

### Domain-Driven Design Implementation

#### Domain Entities
- **Private setters** for all properties to enforce encapsulation
- Public constructor for valid object creation
- Protected parameterless constructor for EF Core
- Domain methods for state changes
- Example:
  ```csharp
  public class Product
  {
      public int Id { get; private set; }
      public string Name { get; private set; } = string.Empty;
      public Money Price { get; private set; } = null!;
      
      protected Product() { } // EF Core
      
      public Product(string name, Money price) // Domain creation
      {
          // Validation and assignment
      }
      
      public void UpdateDetails(string name, Money price) // Domain behavior
      {
          // Business logic and validation
      }
  }
  ```

#### Value Objects
- **Immutable** with private setters
- Static factory methods for creation
- Proper equality implementation
- Example Money value object with currency validation

### CQRS Implementation

#### Command Side (Write Operations)
- **Commands**: Simple DTOs implementing `ICommand` and `IRequest<T>`
- **Command Handlers**: Implement both `ICommandHandler<T>` and `IRequestHandler<T, TResult>`
- **Command Services**: Use EF Core directly for write operations
- **MediatR**: For command/query dispatching

#### Query Side (Read Operations)
- **Queries**: Simple DTOs implementing `IQuery<T>` and `IRequest<T>`
- **Query Handlers**: Implement both `IQueryHandler<T, TResult>` and `IRequestHandler<T, TResult>`
- **Query Services**: Use Dapper for optimized read operations
- **Read Models**: Optimized for data retrieval

#### CQRS Interface Definitions
```csharp
public interface ICommand { }
public interface IQuery<TResult> { }
public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task Handle(TCommand command, CancellationToken cancellationToken = default);
}
public interface IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
}
```

### Libraries and Dependencies

#### Core Libraries
- **Aspire.Hosting.AppHost** (9.3.0+) - Orchestration
- **Aspire.Hosting.SqlServer** (9.3.0+) - Database hosting
- **MediatR** (11.1.0+) - CQRS mediator pattern
- **Serilog.AspNetCore** (9.0.0+) - Structured logging
- **Microsoft.EntityFrameworkCore.SqlServer** (9.0.5+) - ORM for writes
- **Dapper** (2.1.35+) - Micro-ORM for reads
- **dbup-sqlserver** (6.0.0+) - Database migrations

#### API-Specific
- **Microsoft.AspNetCore.OpenApi** (9.0.5+) - Native .NET 9 OpenAPI
- **Swashbuckle.AspNetCore** (6.5.0+) - Swagger UI
- Rate limiting, CORS, health checks (built-in .NET 9)

### Aspire Configuration

#### AppHost Project Structure
- **SDK**: `Microsoft.NET.Sdk` with `Aspire.AppHost.Sdk`
- **IsAspireHost**: `true`
- **UserSecretsId**: Generate unique GUID

#### Orchestration Pattern
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("database");

builder.AddProject<Projects.App_Api>("api")
       .WithReference(db)
       .WaitFor(db);

builder.AddProject<Projects.App_DbMigrator>("migrator")
       .WithReference(db)
       .WaitFor(db);

builder.Build().Run();
```

#### ServiceDefaults Project
- Shared configuration for OpenTelemetry, health checks, service discovery
- Resilience patterns with `AddStandardResilienceHandler()`
- Common middleware and cross-cutting concerns

### Database Management

#### Migration Strategy
- **DbUp** for schema migrations
- Embedded SQL scripts in `DbMigrator` project
- Separate migration service for clean separation
- Migration files named: `0001_CreateTable.sql`, `0002_AddColumn.sql`

#### Data Access Patterns
- **EF Core** for command operations (writes)
- **Dapper** for query operations (reads)
- **Repository Pattern** for domain abstraction
- Connection string resolution priority: `database` → `DockerLearning` → `sqlserver` → `DefaultConnection`

### Testing Strategy

#### Test Types and Frameworks
- **xUnit** as primary testing framework
- **Moq** for mocking dependencies
- **Best.Conventional** for architectural testing
- **Testcontainers.MsSql** for integration tests
- **Microsoft.AspNetCore.Mvc.Testing** for API testing
- **Respawn** for database cleanup between tests

#### Test Organization
```
Tests/
├── Unit/
│   ├── Domain/           # Domain entity and value object tests
│   ├── Application/      # Command/query handler tests
│   └── DTOs/            # Data transfer object tests
├── Integration/         # Full API integration tests
├── Conventions/         # Architectural rule enforcement
└── TestBuilders/        # Test data builders (Builder pattern)
```

#### Convention Testing (Critical)
- **Controllers**: Must end with "Controller"
- **DTOs**: Must end with "Dto" or "ReadModel"
- **Commands**: Must end with "Command"
- **Queries**: Must end with "Query"
- **Services**: Must end with "Service"
- **Repositories**: Must end with "Repository"
- **Domain Entities**: Must have private setters
- **Value Objects**: Must be immutable
- **Async Methods**: Must have "Async" suffix

### API Design Principles

#### Controller Structure
- **Minimal APIs** or traditional controllers
- **ActionResult<T>** return types
- **Model validation** with data annotations
- **OpenAPI** documentation with .NET 9 native support

#### Security and Cross-Cutting Concerns
- **HTTPS redirection** mandatory
- **Security headers** middleware
- **Rate limiting** with fixed window strategy
- **CORS** with environment-specific policies
- **RFC 7807 Problem Details** for standardized error responses
- **Global exception handling** with structured logging

### Logging and Observability

#### Serilog Configuration
- **Console** and **File** sinks for development
- **OpenTelemetry** integration through Aspire
- **Structured logging** with contextual information
- **Log levels**: Information for business operations, Error for exceptions

#### OpenTelemetry Integration
- **Metrics**: ASP.NET Core, HTTP Client, Runtime instrumentation
- **Tracing**: HTTP requests and responses
- **OTLP Exporter**: Configurable via environment variables

### Development Workflow

#### Code Quality Gates
- **TreatWarningsAsErrors**: Enforced across all projects
- **Convention Tests**: Automated architectural rule validation
- **Nullable Reference Types**: Enabled with proper null handling
- **Global Usings**: Consistent across solution

#### Build and Deployment
- **Aspire Dashboard**: For local development observability
- **Service Dependencies**: Proper wait conditions with `WaitFor()`
- **Connection String Management**: Hierarchical resolution strategy
- **Database Migrations**: Automated on startup with proper error handling

#### Error Handling
- **RFC 7807 Problem Details**: Standardized API error responses
- **StatusCodeSelector**: .NET 9 feature for mapping exceptions to HTTP status codes
- **ArgumentException**: Maps to 400 Bad Request
- **KeyNotFoundException**: Maps to 404 Not Found
- **Other exceptions**: Default to 500 Internal Server Error
- **AddProblemDetails()**: Service registration for Problem Details support

### Environment Configuration

#### Application Settings Pattern
- `appsettings.json` (base configuration)
- `appsettings.Docker.json` (container-specific overrides)
- Environment variable support for sensitive data
- User secrets for development

#### Aspire Service Discovery
- Automatic service registration and discovery
- HTTP client configuration with resilience
- Load balancing and health checking

## Implementation Guidelines for LLMs

### Project Creation Sequence
1. Create solution with proper folder structure
2. Set up Domain layer with entities and value objects
3. Implement API layer with CQRS pattern
4. Configure Aspire AppHost with dependencies
5. Add ServiceDefaults for shared configuration
6. Create DbMigrator with embedded scripts
7. Implement comprehensive test suite
8. Set up convention testing with Best.Conventional

### Key Anti-Patterns to Avoid
- **Anemic Domain Models**: Domain objects should have behavior, not just properties
- **Mixed Concerns**: Keep command and query responsibilities separate
- **Tight Coupling**: Use interfaces and dependency injection consistently
- **Missing Validation**: Both at domain and API boundaries
- **Inconsistent Naming**: Follow the established conventions strictly

### Required Files Checklist
- [ ] GlobalUsings.cs in each project
- [ ] Convention tests with Best.Conventional
- [ ] Embedded SQL migration scripts
- [ ] Test builders for complex object creation
- [ ] ServiceDefaults configuration
- [ ] Aspire AppHost with proper dependencies
- [ ] Comprehensive integration tests
- [ ] Structured logging configuration

This template ensures consistency, maintainability, and scalability while following .NET community best practices and modern architectural patterns.

## Required Implementation Standards

- **MUST** use .NET guard clauses for argument validation (ArgumentException.ThrowIfNullOrWhiteSpace, ArgumentNullException.ThrowIfNull)
- **MUST** implement GitHub Actions CI pipeline for automated build and test
- **MUST** configure Serilog with OpenTelemetry sink for Aspire structured logging
- **MUST** implement RFC 7807 Problem Details for standardized API error responses using .NET 9 StatusCodeSelector

## Example Prompts for Development

Use these prompts with Claude Code to create new features following the architecture guidelines:

### Creating a New Domain Entity

```
Create a new Customer domain entity with the following properties:
- Id (int)
- Name (string)
- Email (Email value object)
- DateCreated (DateTime)
- IsActive (bool)

Follow the DDD patterns in this codebase with private setters, proper validation, and domain methods for state changes.
```

### Adding CRUD Operations

```
Add complete CRUD operations for the Customer entity following the CQRS pattern:
- CreateCustomerCommand with handler
- UpdateCustomerCommand with handler
- DeleteCustomerCommand with handler
- GetCustomerQuery with handler
- GetCustomersQuery with handler
- API endpoints in CustomersController
- Include proper validation, error handling, and tests
```

### Creating a New Feature

```
Implement a customer order management feature with:
- Order domain entity with OrderItem value objects
- Commands: CreateOrder, UpdateOrderStatus, CancelOrder
- Queries: GetOrder, GetCustomerOrders, GetOrdersByStatus
- API endpoints with proper validation
- Integration tests covering all scenarios
- Follow the existing patterns for repository, services, and database migrations
```

### Adding Value Objects

```
Create an Address value object with:
- Street, City, State, PostalCode, Country properties
- Validation for required fields and postal code format
- Proper equality implementation
- Static factory method for creation
- Use it in the Customer entity
```

### Database Migrations

```
Create database migrations for the new Customer and Order entities:
- Add embedded SQL scripts in the DbMigrator project
- Follow the naming convention: 0003_CreateCustomers.sql, 0004_CreateOrders.sql
- Include proper indexes and foreign key relationships
- Update the connection string resolution if needed
```