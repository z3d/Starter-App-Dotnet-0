# .NET 9 Clean Architecture Template

This document outlines the architectural patterns, conventions, and technical standards for a .NET 9 project template using **Aspire** orchestration. It serves as comprehensive guidance for implementing Clean Architecture with CQRS and Domain-Driven Design principles.

## Project Overview

**Clean Architecture** .NET 9 solution implementing:
- **CQRS Pattern**: Separate command/query responsibilities
- **Domain-Driven Design**: Rich domain models with business logic
- **Aspire Orchestration**: Service orchestration and observability
- **Reproducible Builds**: Package lock files and centralized configuration

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

## Build Configuration & Standards

### Centralized Configuration (Directory.Build.props)

All projects inherit shared MSBuild settings from `Directory.Build.props`:

#### **Framework & Language Features**
- **Target Framework**: `.NET 9.0` across all projects
- **Nullable Reference Types**: Enabled solution-wide
- **Implicit Usings**: Enabled with project-specific `GlobalUsings.cs`
- **Deterministic Builds**: Consistent build outputs across environments

#### **Code Quality Enforcement**
- **Treat Warnings as Errors**: Enabled solution-wide
- **Global Analyzers**: Microsoft.CodeAnalysis.Analyzers across all projects
- **Debug Configuration**: Portable PDBs with consistent debug symbols

#### **Package Management**
- **Package Lock Files**: `RestorePackagesWithLockFile=true` for reproducible builds
- **CI/CD Integration**: `RestoreLockedMode` enabled in build environments
- **Dependency Security**: Hash verification of packages through lock files

### Code Formatting Standards (.editorconfig)

- **Line Length**: 180 characters maximum
- **File-Scoped Namespaces**: Enforced solution-wide
- **Using Directives**: System usings first, alphabetical ordering
- **StyleCop Integration**: SA1200, SA1209, SA1210, SA1211 enabled
- **Consistent Formatting**: Mixed rule ordering with specific overrides

### Pre-Commit Checklist

**ALWAYS** complete before committing:

1. **Code Formatting**: `dotnet format` - Apply formatting standards
2. **Build Verification**: `dotnet build` - Ensure compilation success
3. **Test Execution**: `dotnet test` - Verify all tests pass (171+ tests)
4. **Package Updates**: `dotnet restore` - Update lock files if dependencies changed
5. **Lock File Verification**: Ensure `packages.lock.json` files are committed

## Architecture Principles & Anti-Patterns

### Core Design Principles

#### **Domain-Driven Design**
- **Rich Domain Models**: Entities contain business behavior, not just properties
- **Value Objects**: Immutable objects with business meaning (Email, Money)
- **Aggregate Roots**: Control access and maintain consistency
- **Domain Services**: For operations that don't belong to a single entity

#### **CQRS Implementation**
- **Pure Separation**: Commands → DTOs, Queries → ReadModels
- **No Mixing**: Never return DTOs from queries or ReadModels from commands
- **Performance Optimization**: Commands use EF Core, Queries use Dapper
- **Clear Boundaries**: Separate optimization strategies for reads vs writes

#### **Clean Architecture**
- **Dependency Inversion**: Domain layer has no external dependencies
- **Interface Segregation**: Small, focused interfaces
- **Single Responsibility**: Each class has one reason to change
- **Explicit over Magic**: Prefer explicit code over convention-based "magic"

### Prohibited Anti-Patterns

#### **NEVER Use These Libraries**
- ❌ **AutoMapper**: Use explicit mapping code instead
- ❌ **MediatR**: Use custom mediator (commercial licensing issues)
- ❌ **Commercial Libraries**: Without explicit stakeholder approval

#### **NEVER Use These Patterns**
- ❌ **Anemic Domain Models**: Domain objects must have behavior
- ❌ **Mixed CQRS Concerns**: Keep commands and queries strictly separate
- ❌ **Code Regions**: Use proper class structure instead
- ❌ **Historical Comments**: Use git history and meaningful commits
- ❌ **Dual Representation**: Avoid separate entity/value object pairs
- ❌ **XML Documentation Comments**: Not needed for application projects (only for libraries)

#### **Code Quality Standards**
- ❌ **Tight Coupling**: Use dependency injection and interfaces
- ❌ **Missing Validation**: Validate at both domain and API boundaries
- ❌ **Inconsistent Naming**: Follow established conventions strictly
- ❌ **Third-Party Dependencies**: Prefer .NET native libraries

## Domain-Driven Design Implementation

### Domain Entities

**Core Pattern**: Entities with private setters, public constructors, and domain behavior

```csharp
public class Product
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public int Stock { get; private set; }

    protected Product() { } // EF Core constructor

    public Product(string name, string description, Money price, int stock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentOutOfRangeException.ThrowIfNegative(stock);

        Name = name;
        Description = description;
        Price = price;
        Stock = stock;
    }

    public void UpdateDetails(string name, Money price)
    {
        // Domain validation and business rules
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        
        Name = name;
        Price = price;
    }
}
```

### Value Objects

**Core Pattern**: Immutable objects with static factory methods and proper equality

```csharp
public class Money
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        
        if (currency.Length > 3)
            throw new ArgumentException("Currency code cannot exceed 3 characters", nameof(currency));
            
        return new Money(amount, currency);
    }

    // Equality implementation, operators, etc.
}
```

### Entity-Value Object Integration

**Use Single Entity with Embedded Value Objects** (not dual representation):

```csharp
// ✅ CORRECT - Single entity with embedded value objects
public class Customer
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Email Email { get; private set; } = null!; // Embedded value object

    public void UpdateEmail(Email newEmail) 
    {
        ArgumentNullException.ThrowIfNull(newEmail);
        Email = newEmail;
    }
}

// ❌ AVOID - Dual representation creates unnecessary complexity
public class CustomerValue { } // Don't create this
public class CustomerEntity { } // When you already have Customer above
```

## CQRS Implementation

### Core CQRS Patterns

#### **Pure CQRS Separation**
- **Commands → DTOs**: Write operations return DTOs for client communication
- **Queries → ReadModels**: Read operations return ReadModels optimized for display
- **Never Mix**: Don't return DTOs from queries or ReadModels from commands

#### **Interface Definitions**

```csharp
public interface ICommand { }
public interface IQuery<TResult> { }

public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

public interface IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
```

#### **Return Type Standards**

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

### Command Implementation

**Direct Entity Framework Usage** - No intermediate service layers:

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
        
        return MapToDto(customer); // Explicit mapping
    }
}
```

### Query Implementation

**Dapper for Optimized Reads**:

```csharp
public class GetCustomerQueryHandler : IRequestHandler<GetCustomerQuery, CustomerReadModel>
{
    private readonly IDbConnection _connection;

    public GetCustomerQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<CustomerReadModel> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, Name, Email FROM Customers WHERE Id = @Id";
        
        var customer = await _connection.QuerySingleOrDefaultAsync<CustomerReadModel>(sql, new { query.Id });
        
        if (customer == null)
            throw new KeyNotFoundException($"Customer with ID {query.Id} not found");
            
        return customer;
    }
}
```

### Auto-Registration Pattern

**Reflection-Based Handler Discovery**:

```csharp
// In Program.cs - Single line registers ALL handlers
builder.Services.AddMediator(Assembly.GetExecutingAssembly());

// Auto-discovers and registers:
// ✅ All command handlers implementing IRequestHandler<,>
// ✅ All query handlers implementing IRequestHandler<,>
// ✅ Zero manual registration needed
// ✅ New handlers automatically discovered
```

## Data Access & Entity Framework

### EF Core Configuration

**Value Object Embedding** with `OwnsOne`:

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
```

### Database Migrations

**DbUp with Embedded Scripts**:

- SQL scripts embedded in `DbMigrator` project
- Sequential naming: `0001_CreateTables.sql`, `0002_AddIndexes.sql`
- Automatic execution on startup with error handling
- Separate migration service for clean separation

### Connection String Resolution

Priority order: `database` → `DockerLearning` → `sqlserver` → `DefaultConnection`

## Technology Stack

### Core Dependencies

#### **Framework & Hosting**
- **.NET 9.0**: Latest framework with performance improvements
- **Aspire.Hosting.AppHost** (9.3.0+): Service orchestration
- **Aspire.Hosting.SqlServer** (9.3.0+): Database container management

#### **Data Access**
- **Entity Framework Core 9.0.5+**: Write operations and migrations
- **Dapper 2.1.35+**: Optimized read operations
- **Microsoft.Data.SqlClient**: SQL Server connectivity

#### **Logging & Observability**
- **Serilog.AspNetCore 9.0.0+**: Structured logging
- **OpenTelemetry**: Metrics, tracing, and telemetry
- **Aspire Dashboard**: Development-time observability

#### **API & Documentation**
- **Microsoft.AspNetCore.OpenApi 9.0.5+**: Native .NET 9 OpenAPI
- **Swashbuckle.AspNetCore 6.5.0+**: Swagger UI generation

#### **Testing**
- **xUnit**: Primary testing framework
- **Testcontainers.MsSql**: Database integration testing
- **Microsoft.AspNetCore.Mvc.Testing**: API integration testing
- **Moq**: Mocking framework
- **Best.Conventional**: Architectural rule enforcement

### Custom Implementation

#### **Mediator Pattern**
**Custom CQRS Mediator** (replaces commercial MediatR):

```csharp
public interface IMediator
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}

public class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    
    public async Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResult));
        var handler = _serviceProvider.GetRequiredService(handlerType);
        
        var method = handlerType.GetMethod("HandleAsync");
        var task = (Task<TResult>)method!.Invoke(handler, new object[] { request, cancellationToken })!;
        
        return await task;
    }
}
```

**Benefits**:
- No commercial licensing concerns
- Simple, transparent implementation
- Full control over dispatching logic
- Zero reflection overhead in handlers

## Aspire Configuration

### AppHost Project Setup

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);
var database = sql.AddDatabase("database");

var api = builder.AddProject<Projects.StarterApp_Api>("api")
                 .WithReference(database)
                 .WaitFor(database);

var migrator = builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
                      .WithReference(database)
                      .WaitFor(database);

builder.Build().Run();
```

### ServiceDefaults Configuration

**Shared cross-cutting concerns**:
- OpenTelemetry instrumentation (ASP.NET Core, HTTP, Runtime)
- Service discovery and load balancing
- Resilience patterns with circuit breakers
- Health check endpoints
- Common middleware registration

## Testing Strategy

### Test Organization

```
Tests/
├── Domain/              # Entity and value object tests
├── Application/         # Command/query handler tests  
├── Integration/         # Full API integration tests
├── Conventions/         # Architectural rule enforcement
└── TestBuilders/        # Test data builders
```

### Convention Testing

**Architectural Rule Enforcement** with Best.Conventional:

```csharp
[Fact]
public void Controllers_Should_EndWith_Controller()
{
    Types.InAssembly(ApiAssembly)
        .That()
        .InheritFrom<ControllerBase>()
        .Should()
        .HaveNameEndingWith("Controller")
        .Check();
}

[Fact]
public void Commands_Should_EndWith_Command()
{
    Types.InAssembly(ApiAssembly)
        .That()
        .ImplementInterface(typeof(ICommand))
        .Should()
        .HaveNameEndingWith("Command")
        .Check();
}
```

### Integration Testing

**Testcontainers for Realistic Testing**:

```csharp
public class ApiTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _msSqlContainer;
    private WebApplicationFactory<Program> _factory;

    public async Task InitializeAsync()
    {
        await _msSqlContainer.StartAsync();
        
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ConnectionStrings>(options =>
                    {
                        options.DefaultConnection = _msSqlContainer.GetConnectionString();
                    });
                });
            });
    }
}
```

## API Design Standards

### Controller Patterns

```csharp
[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> CreateCustomer(
        CreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.SendAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetCustomer), new { id = result.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CustomerReadModel>> GetCustomer(
        int id, 
        CancellationToken cancellationToken)
    {
        var query = new GetCustomerQuery { Id = id };
        var result = await _mediator.SendAsync(query, cancellationToken);
        return Ok(result);
    }
}
```

### Error Handling

**RFC 7807 Problem Details** with .NET 9 StatusCodeSelector:

```csharp
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = (context) =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

// Automatic status code mapping
// ArgumentException → 400 Bad Request  
// KeyNotFoundException → 404 Not Found
// Other exceptions → 500 Internal Server Error
```

## Development Workflow Guidelines

### Code Quality Standards

#### **Testing Requirements**
- **ALWAYS** run tests before committing changes
- All tests must pass (171+ tests in current implementation)
- Write tests for new domain objects following existing patterns
- Use integration tests for API endpoints

#### **Code Duplication Guidelines**  
- **Duplication over wrong abstraction** - Bad abstractions are harder to fix
- **Consider domain boundaries** - Similar code in different contexts should stay separate
- **Evaluate coupling costs** - Sometimes shared code creates expensive coupling
- **Optimize for readability** - Sometimes duplication is clearer than complex abstraction

#### **Documentation Maintenance**
- **Live document** - Update CLAUDE.md with every architectural change
- **Document WHY not WHAT** - Explain reasoning behind decisions
- **Remove obsolete guidance** - Delete sections that no longer apply
- **Update examples** - Replace old patterns with current approaches

### Development Commands

```bash
# Format code and remove unnecessary imports
dotnet format

# Build entire solution  
dotnet build

# Run all tests
dotnet test

# Update package lock files
dotnet restore --use-lock-file

# Use locked mode (CI/CD)
dotnet restore --locked-mode
```

This template ensures consistency, maintainability, and scalability while following .NET community best practices and modern architectural patterns.
