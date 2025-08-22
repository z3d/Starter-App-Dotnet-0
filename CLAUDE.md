# .NET Project Architecture Template

This document outlines the architectural patterns, conventions, and technical approach for creating a .NET 9 project template using **Aspire** (not Docker) orchestration. This serves as comprehensive instructions for LLMs creating similar projects.

## Project Overview

This is a **Clean Architecture** .NET 9 solution implementing **CQRS** with **Domain-Driven Design** principles, using **Aspire** for orchestration and observability.

### Key Anti-Patterns to Avoid

- **Anemic Domain Models**: Domain objects should have behavior, not just properties
- **Mixed Concerns**: Keep command and query responsibilities separate - Commands→DTOs, Queries→ReadModels
- **Tight Coupling**: Use interfaces and dependency injection consistently
- **Missing Validation**: Both at domain and API boundaries
- **Inconsistent Naming**: Follow the established conventions strictly
- **Dual Representation Overengineering**: Avoid creating separate entity/value object pairs when a single entity with embedded value objects suffices (see Architecture Consistency section below)
- **Code Regions**: Never use #region/#endregion directives - organize code through proper class structure, methods, and logical separation instead
- **AutoMapper**: NEVER use AutoMapper or any automatic object mapping libraries - use explicit mapping code instead
- **XML Documentation Comments**: Never use XML documentation comments (/// <summary>, /// <param>, etc.) - they are verbose, inconsistent when used partially, and interfere with code readability. Let good naming and clean code structure be self-documenting instead
- **MediatR**: NEVER use MediatR - the author made it commercial, eliminating the free open source benefits. Additionally, it adds unnecessary complexity, reflection overhead, and indirection. Use a simple custom mediator pattern instead. Our custom implementation provides the same benefits (decoupling, testability) without the commercial dependency and bloat
- **Historical Comments**: NEVER add comments like "// Create entity directly (previously in command service)" - source control already tracks these changes and such comments become noise over time
- **Third-Party Library Dependencies**: Prefer native .NET libraries over third-party packages, especially those with single maintainers or uncertain long-term support. Avoid external dependencies that can become maintenance burdens, security risks, or points of failure. When third-party libraries are necessary, choose well-established, multi-maintainer projects with strong community support
- **Commercial Libraries**: NEVER use commercial libraries without explicit permission from project stakeholders. Commercial dependencies create licensing compliance issues, ongoing costs, and potential legal risks. Always verify licensing terms and get explicit approval before introducing any commercial dependencies

### Code Documentation Philosophy

#### Source Control Is History
- **Git tracks changes** - no need for "previously was X" comments
- **Commit messages provide context** - use meaningful commit messages instead of inline history
- **Code should be self-documenting** - focus on clear naming and structure

#### When Refactoring - Document Preferences Automatically
- **Update CLAUDE.md immediately** when architectural decisions change
- **Document WHY not WHAT** - explain reasoning behind architectural choices
- **Add to anti-patterns section** if avoiding something specific
- **Update examples** to reflect current approach

#### Comment Guidelines
- **Business Logic Only**: Comments should explain complex business rules or domain constraints
- **Public API Boundaries**: Brief descriptions for controllers and major service interfaces
- **No Implementation History**: Remove comments about previous implementations
- **Temporary TODOs**: Use TODO comments sparingly and remove them quickly

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

#### ✅ Pure CQRS Pattern Implementation

**CRITICAL: This project implements PURE CQRS with strict separation:**

- **Commands → DTOs**: All write operations (POST, PUT, DELETE) return DTOs for client communication
- **Queries → ReadModels**: All read operations (GET) return ReadModels optimized for display
- **NO MIXING**: Never return DTOs from queries or ReadModels from commands

##### Return Type Rules
```csharp
// ✅ CORRECT - Commands return DTOs
public async Task<ActionResult<CustomerDto>> CreateCustomer(CreateCustomerCommand command)
public async Task<ActionResult<ProductDto>> UpdateProduct(int id, UpdateProductCommand command)

// ✅ CORRECT - Queries return ReadModels  
public async Task<ActionResult<CustomerReadModel>> GetCustomer(int id)
public async Task<ActionResult<IEnumerable<ProductReadModel>>> GetAllProducts()

// ❌ WRONG - Mixed concerns
public async Task<ActionResult<CustomerDto>> GetCustomer(int id)      // Should be ReadModel
public async Task<ActionResult<CustomerReadModel>> CreateCustomer()   // Should be DTO
```

##### Benefits Achieved
- **Performance**: Direct database-to-ReadModel mapping eliminates conversion overhead
- **Clarity**: Clear architectural boundaries between read and write operations
- **Maintainability**: Separate optimization strategies for commands vs queries
- **API Design**: Read endpoints optimized for client consumption

##### ⚠️ NEVER USE AUTOMAPPER
**PROHIBITED**: AutoMapper or any automatic object mapping libraries are **STRICTLY FORBIDDEN**:
- Creates hidden complexity and runtime failures
- Obscures data transformation logic  
- Makes debugging difficult
- Violates explicit architecture principles
- **USE EXPLICIT MAPPING**: Write clear, explicit mapping code instead

### Service Registration Pattern

#### Auto-Registration via Reflection

**ARCHITECTURE ACHIEVEMENT**: Complete handler auto-registration eliminates manual service registration:

- **Automatic Discovery**: All handlers implementing `IRequestHandler<,>` are automatically registered via reflection
- **NO MANUAL REGISTRATION**: Command/Query handlers require zero explicit service registration
- **Symmetric Pattern**: Both commands and queries auto-register consistently
- **Zero Maintenance**: New handlers are automatically discoverable without code changes

##### ✅ COMPLETE AUTO-REGISTRATION
```csharp
// In Program.cs - ONLY line needed for ALL handlers
builder.Services.AddMediator(Assembly.GetExecutingAssembly());

// NO manual registrations needed - ALL handlers auto-discovered:
// ✅ CreateCustomerCommandHandler -> auto-registered
// ✅ UpdateCustomerCommandHandler -> auto-registered  
// ✅ DeleteCustomerCommandHandler -> auto-registered
// ✅ GetCustomerQuery -> auto-registered
// ✅ All other handlers -> auto-registered
```

##### Command Handler Architecture

**CRITICAL: Use direct Entity Framework approach in command handlers:**

- **Direct EF Usage**: All command handlers use `ApplicationDbContext` directly
- **NO INTERMEDIATE SERVICES**: Command service layer was eliminated for simplicity
- **Entity Framework IS the Repository**: EF Core already provides Repository/Unit of Work patterns
- **Consistent Dependencies**: All handlers only depend on `ApplicationDbContext`

##### ✅ CURRENT ARCHITECTURE - Direct EF in Handlers
```csharp
public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly ApplicationDbContext _dbContext;

    public CreateCustomerCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CustomerDto> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        var customer = new Customer(command.Name, email);
        
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        return MapToDto(customer);
    }
}
```

##### ❌ ELIMINATED - Unnecessary Command Service Layer
```csharp
// REMOVED: Command services were eliminated as unnecessary abstraction
public class CustomerCommandService : ICustomerCommandService // ❌ DELETED
{
    private readonly ApplicationDbContext _dbContext;
    // This was unnecessary thin wrapper over EF Core
}
```

#### Architecture Evolution Summary

**REFACTORING COMPLETED**: Command service layer elimination achieved full architectural consistency:

1. **Before**: Asymmetric registration pattern
   - ❌ Queries auto-registered via reflection
   - ❌ Commands required manual service registration
   - ❌ Command handlers used command services as thin wrappers

2. **After**: Symmetric auto-registration pattern
   - ✅ **ALL handlers auto-register** via reflection 
   - ✅ **Zero manual registrations** needed
   - ✅ **Direct EF Core usage** in all handlers
   - ✅ **Consistent architecture** across all operations
   - ✅ **All 158 tests passing** - no functionality lost

3. **Benefits Achieved**:
   - **Simplified Architecture**: Eliminated unnecessary abstraction layer
   - **Reduced Complexity**: Fewer files, fewer dependencies, clearer code paths
   - **Consistent Patterns**: Same approach for all CQRS operations
   - **Better Testability**: Direct EF usage easier to test with in-memory databases
   - **Zero Maintenance Registration**: New handlers automatically discovered

### Data Access Pattern

#### Entity Framework Direct Usage

**CRITICAL ARCHITECTURAL PRINCIPLE**: Use Entity Framework Core directly without additional repository abstractions:

- **EF Core IS the Repository**: Entity Framework already implements Repository and Unit of Work patterns
- **NO EXTRA REPOSITORIES**: Don't wrap EF DbContext in custom repository interfaces
- **Direct Context Usage**: Inject `ApplicationDbContext` directly into handlers
- **Simplicity Over Abstraction**: Fewer layers = less complexity and better performance

**Service Registration Strategy:**
- **Small Scale (<10 services)**: Use explicit manual registration for clarity and control
- **Large Scale (10+ services)**: Consider convention-based registrar pattern to reduce boilerplate
- **Always**: Prefer explicitness over "magic" - if you can't easily see what's registered, it's too complex

#### Query Side

- **Query Handlers**: Individual handlers for each query using Dapper for optimized reads
- **Auto-Registration**: Query handlers are automatically registered via `AddMediator()`
- **No Service Layer**: Queries go directly to handlers, not through service classes

### Libraries and Dependencies

#### Core Libraries

- **Aspire.Hosting.AppHost** (9.3.0+) - Orchestration
- **Aspire.Hosting.SqlServer** (9.3.0+) - Database hosting
- **Custom Mediator** - Simple CQRS mediator pattern (replaces commercial MediatR)
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
- **Dual Representation Overengineering**: Avoid creating separate entity/value object pairs when a single entity with embedded value objects suffices (see Architecture Consistency section below)

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

## Architecture Consistency Guidelines

### Entity-Value Object Pattern

**CRITICAL**: Maintain consistent patterns across all domain entities. Follow the established approach used by Customer and Product entities:

#### ✅ Correct Pattern (Single Entity + Embedded Value Objects)
```csharp
public class Product  // Entity with identity
{
    public int Id { get; private set; }
    public Money Price { get; private set; }  // Embedded value object
    
    public void UpdatePrice(Money newPrice) { /* domain logic */ }
}

public class Customer  // Entity with identity  
{
    public int Id { get; private set; }
    public Email Email { get; private set; }  // Embedded value object
    
    public void UpdateEmail(Email newEmail) { /* domain logic */ }
}

public class OrderItem  // Entity with identity
{
    public int Id { get; private set; }
    public Money UnitPrice { get; private set; }  // Embedded value object
    
    public Money GetTotalPrice() { /* domain logic */ }
}
```

#### ❌ Overengineered Anti-Pattern (Dual Representation)
```csharp
// DON'T DO THIS - Creates unnecessary complexity
public class OrderItemValue { /* value object */ }
public class OrderItemEntity { /* separate entity */ }
// + Complex conversion logic between the two
```

### EF Core Configuration Consistency

Use `OwnsOne` for embedded value objects consistently:

```csharp
// Product configuration
modelBuilder.Entity<Product>()
    .OwnsOne(p => p.Price, priceBuilder => {
        priceBuilder.Property(m => m.Amount).HasColumnName("PriceAmount");
        priceBuilder.Property(m => m.Currency).HasColumnName("PriceCurrency");
    });

// Customer configuration  
modelBuilder.Entity<Customer>()
    .OwnsOne(c => c.Email, emailBuilder => {
        emailBuilder.Property(e => e.Value).HasColumnName("Email");
    });

// OrderItem configuration (follows same pattern)
modelBuilder.Entity<OrderItem>()
    .OwnsOne(oi => oi.UnitPrice, priceBuilder => {
        priceBuilder.Property(m => m.Amount).HasColumnName("UnitPrice");
        priceBuilder.Property(m => m.Currency).HasColumnName("Currency");
    });
```

### When to Use Each Approach

**Use Single Entity (Recommended):**
- Entity has clear identity (ID, lifecycle)
- Business logic belongs to the entity
- Embedded value objects provide structure
- Database table represents the entity

**Avoid Dual Representation Unless:**
- Extremely complex domain requirements
- Clear separation needed between persistence/domain models
- Team has deep DDD expertise
- Benefits outweigh complexity costs

### Architecture Verification Checklist

When adding new domain concepts, verify consistency:

- [ ] Does it follow Customer/Product/OrderItem pattern?
- [ ] Are value objects embedded, not separate entities?
- [ ] Is EF Core configuration consistent with existing entities?
- [ ] Are business methods on the entity, not external services?
- [ ] Can developers easily understand the relationship to existing code?

## Required Implementation Standards

- **MUST** use .NET guard clauses for argument validation (ArgumentException.ThrowIfNullOrWhiteSpace, ArgumentNullException.ThrowIfNull)
- **MUST** implement GitHub Actions CI pipeline for automated build and test
- **MUST** configure Serilog with OpenTelemetry sink for Aspire structured logging
- **MUST** implement RFC 7807 Problem Details for standardized API error responses using .NET 9 StatusCodeSelector
- **MUST** write comprehensive integration tests for all new domain objects following existing patterns
- **MUST** examine and follow existing test patterns when creating new tests (check Domain/, Integration/, and TestBuilders/ folders)
- **MUST** always run tests after complex changes such as adding new domain objects to the API
- **MUST** persist when tests fail - fix issues rather than commenting out code or taking shortcuts
- **MUST** ensure all tests pass before considering a task complete

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

Follow the DDD patterns in this codebase with private setters, proper validation, and domain methods for state changes. Include comprehensive unit tests following the existing patterns in the Domain/ test folder.
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
- Include proper validation, error handling, and comprehensive tests following existing patterns
- Write integration tests examining existing Integration/ test folder patterns
```

### Creating a New Feature

```
Implement a customer order management feature with:
- Order domain entity with OrderItem value objects
- Commands: CreateOrder, UpdateOrderStatus, CancelOrder
- Queries: GetOrder, GetCustomerOrders, GetOrdersByStatus
- API endpoints with proper validation
- Comprehensive unit and integration tests covering all scenarios
- Follow the existing patterns for repository, services, database migrations, and test organization
- Examine existing test builders and patterns before implementing new tests
```

### Adding Value Objects

```
Create an Address value object with:
- Street, City, State, PostalCode, Country properties
- Validation for required fields and postal code format
- Proper equality implementation
- Static factory method for creation
- Use it in the Customer entity
- Write comprehensive unit tests following existing value object test patterns
```

### Database Migrations

```
Create database migrations for the new Customer and Order entities:
- Add embedded SQL scripts in the DbMigrator project
- Follow the naming convention: 0003_CreateCustomers.sql, 0004_CreateOrders.sql
- Include proper indexes and foreign key relationships
- Update the connection string resolution if needed
```

### Architecture Pattern Reference

**Order Management Implementation**: Use the Order/OrderItem entities as reference for proper DDD implementation:
- Order entity manages OrderItem collection with business rules
- OrderItem entity has embedded Money value object for pricing
- EF Core uses OwnsOne for value objects, HasMany/WithOne for entity relationships
- Single entities with embedded value objects (no dual representation)
- Business logic methods directly on entities (GetTotalPrice, AddItem, etc.)
- Proper encapsulation with private setters and domain validation

## Documentation Maintenance

### Auto-Documentation During Refactoring

**CRITICAL**: When making architectural changes, immediately update this document:

1. **Add Anti-Patterns**: If avoiding something, add to "Key Anti-Patterns to Avoid" section
2. **Update Examples**: Replace old patterns with new preferred approaches  
3. **Document Reasoning**: Explain WHY the change improves the architecture
4. **Remove Obsolete Guidance**: Delete sections that no longer apply

### Documentation Philosophy

- **Live Document**: This file should evolve with every major architectural decision
- **Source of Truth**: CLAUDE.md is the authoritative architectural guide
- **No Historical Comments in Code**: Use git history and commit messages instead
- **Immediate Updates**: Update documentation as part of the same commit that makes architectural changes
- **Example-Driven**: Always provide concrete code examples of preferred patterns

### Refactoring Checklist

When refactoring architecture:
- [ ] Remove historical comments from code (git tracks changes)
- [ ] Update CLAUDE.md anti-patterns section  
- [ ] Update CLAUDE.md examples to show new approach
- [ ] Add reasoning for why the change was made
- [ ] Remove obsolete guidance from documentation
- [ ] **Run all tests before pushing changes** - ensure no functionality is broken
- [ ] Update any affected documentation sections

## Development Guidelines

### Testing Philosophy
**Always run tests before pushing changes** - This is non-negotiable for LLM development workflows. Every code change should be validated through the test suite before being pushed or committed to source control. Tests are your safety net and documentation of expected behavior.

### Code Duplication vs DRY Principle
**Duplication of code is OK sometimes** - Don't re-use solely for the sake of DRY (Don't Repeat Yourself). Use engineering judgment to decide when duplication is appropriate:

- **Prefer duplication over wrong abstraction** - A bad abstraction is harder to fix than duplicated code
- **Consider domain boundaries** - Code that looks similar but serves different business contexts should often remain separate
- **Evaluate coupling cost** - Sometimes the coupling introduced by sharing code is more expensive than maintaining duplicate code
- **Think about change patterns** - If two pieces of code change for different reasons, they should probably stay separate
- **Optimize for readability** - Sometimes duplicated code is clearer than a complex shared abstraction

**When to extract commonality**:
- Clear, stable abstraction emerges naturally
- Multiple pieces of code change together consistently  
- The abstraction has a clear single responsibility
- The benefits outweigh the coupling costs
