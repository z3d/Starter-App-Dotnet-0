# .NET 10 Clean Architecture Template

This document outlines the architectural patterns, conventions, and technical standards for a .NET 10 project template using **Aspire** orchestration. It serves as comprehensive guidance for implementing Clean Architecture with CQRS and Domain-Driven Design principles.

## Project Overview

**Clean Architecture** .NET 10 solution implementing:
- **Minimal APIs**: Modern .NET 10 endpoint-based architecture with high performance
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
│   │   │   ├── CustomerEndpoints.cs   # Customer management endpoints
│   │   │   ├── OrderEndpoints.cs      # Order processing endpoints
│   │   │   ├── ProductEndpoints.cs    # Product catalog endpoints
│   │   │   ├── IEndpointDefinition.cs # Endpoint definition contract
│   │   │   ├── EndpointExtensions.cs  # Auto-discovery extensions
│   │   │   └── Filters/                # Endpoint filters
│   │   ├── Application/                # CQRS commands and queries
│   │   ├── Data/                       # EF Core DbContext
│   │   └── Infrastructure/             # Cross-cutting concerns
│   ├── [ProjectName].Domain/           # Domain Layer (Core Business Logic)
│   ├── [ProjectName].AppHost/          # Aspire Orchestration Host
│   ├── [ProjectName].ServiceDefaults/  # Shared Aspire Service Configuration
│   ├── [ProjectName].DbMigrator/       # Database Migration Tool
│   └── [ProjectName].Tests/            # Comprehensive Test Suite
└── docs/                               # Documentation
    └── API-ENDPOINTS.md                # Detailed endpoint documentation
```

## Build Configuration & Standards

### Centralized Configuration (Directory.Build.props)

All projects inherit shared MSBuild settings from `Directory.Build.props`:

#### **Framework & Language Features**
- **Target Framework**: `.NET 10.0` across all projects
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
3. **Test Execution**: `dotnet test` - Verify all tests pass (204+ tests)
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
- ❌ **Repository Pattern**: Use DbContext directly in command handlers — removed in favour of simpler, more explicit data access without an unnecessary abstraction layer
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

### Reconstitute Pattern

**Static factory method for rebuilding aggregates from database rows**. Used when loading entities that already exist in the database and may be in any valid state (including states that the public constructor wouldn't allow creating from scratch).

```csharp
// On the Order aggregate:
public static Order Reconstitute(int id, int customerId, DateTime orderDate,
    OrderStatus status, DateTime lastUpdated, List<OrderItem> items)
{
    var order = new Order
    {
        Id = id, CustomerId = customerId, OrderDate = orderDate,
        Status = status, LastUpdated = lastUpdated
    };
    order._items.AddRange(items);
    order.Items = order._items.AsReadOnly();
    return order;
}
```

**Why it exists**: The public `Order(customerId)` constructor enforces creation-time invariants (status = Pending, empty items). When loading an order from the database that's already in `Shipped` status with 5 items, the constructor can't be used. `Reconstitute` bypasses creation validation because the data was already validated when it was originally created.

**When to use**: In command handlers when loading aggregates from the database for mutation. Not needed for queries (which use Dapper and ReadModels).

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

### Data Access Design

**Commands use DbContext directly** — no repository abstraction. This was a deliberate decision:
- DbContext already implements Unit of Work and Repository patterns internally
- A repository layer added indirection without value (only 2 of 9 handlers used it)
- Direct DbContext usage is simpler, more explicit, and easier to debug
- Entities are loaded with `AsNoTracking()` then reconstituted via domain factory methods

**Queries use Dapper** for optimised reads directly against SQL, returning lightweight ReadModels.

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
- **.NET 10.0**: Latest framework with performance improvements
- **Aspire.Hosting.AppHost** (13.1.0+): Service orchestration with GenAI visualizer and multi-resource logs
- **Aspire.Hosting.SqlServer** (13.1.0+): Database container management
- **Aspire.Hosting.Seq** (13.1.0+): Structured logging and observability
- **Aspire.Hosting.DevTunnels** (13.1.0+): Expose local services to the internet for testing

#### **Data Access**
- **Entity Framework Core 10.0.3+**: Write operations and migrations
- **Dapper 2.1.35+**: Optimized read operations
- **Microsoft.Data.SqlClient**: SQL Server connectivity

#### **Logging & Observability**
- **Serilog.AspNetCore 10.0.0+**: Structured logging
- **OpenTelemetry**: Metrics, tracing, and telemetry
- **Aspire Dashboard**: Development-time observability

#### **API & Documentation**
- **Microsoft.AspNetCore.OpenApi 10.0.3+**: Native .NET 10 OpenAPI
- **Scalar.AspNetCore 2.11+**: API reference UI (at `/scalar/v1`)

#### **Testing**
- **xUnit**: Primary testing framework
- **FsCheck 2.16.6 + FsCheck.Xunit**: Property-based (fuzz) testing
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

### Aspire 13.1.2 Features

**Current Version**: Aspire 13.1.2 (.NET 10 release)

**Key Features**:
- **CLI Tools**: New `aspire update` command for automatic package updates
- **Dashboard**: GenAI visualizer for LLM telemetry, multi-resource console logs, custom resource icons
- **Integrations**: OpenAI hosting, GitHub Models, Azure AI Foundry, Dev Tunnels support
- **Deployment**: Azure Container App Jobs, built-in Azure deployment via CLI
- **YARP**: Static file serving capabilities alongside reverse proxy functionality

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
├── Fuzzing/             # Property-based tests (FsCheck)
└── TestBuilders/        # Test data builders
```

### Property-Based Testing (Fuzzing)

**FsCheck 2.16.6** with **FsCheck.Xunit** integration for property-based testing. Instead of hand-picked test values, FsCheck generates hundreds of random inputs to verify domain invariants hold universally.

**Key properties tested**:
- **Money**: Addition commutativity/associativity, subtract-inverse, negative rejection, currency validation
- **Email**: Whitespace rejection, valid acceptance, equality reflexivity, random string robustness
- **Product**: Stock update round-trip (`+n` then `-n` restores original), over-deduction throws
- **OrderItem**: Price invariant (`TotalIncGst == UnitIncGst * Qty`), GST rate bounds, ID/quantity validation
- **Order State Machine**: Valid transition paths never throw, invalid transitions always throw, Reconstitute preserves all properties, order totals equal sum of item totals

**Usage**: Tests use `[Property]` attribute (FsCheck.Xunit) instead of `[Fact]`. Convention tests detect both attributes. FsCheck automatically shrinks failing inputs to minimal counterexamples.

### Convention Testing

**Architectural Rule Enforcement** with [Best.Conventional](https://github.com/andrewabest/Conventional) (`Conventions/` directory, 21 tests across `NamingConventionTests`, `CqrsConventionTests`, `DomainConventionTests`).

**Approach**: Use built-in conventions where possible. For structural checks not covered by built-ins, create custom conventions extending `ConventionSpecification` (from `Conventional.Conventions` namespace) to stay consistent with the library's fluent `MustConformTo` / `WithFailureAssertion` API.

#### Convention Categories

**Naming Conventions** — built-in `Convention.NameMustEndWith`:
```csharp
// Endpoints, Commands, Queries, Handlers, Services, Validators, DTOs/ReadModels, Test classes
endpointTypes.MustConformTo(Convention.NameMustEndWith("Endpoints"));
commandTypes.MustConformTo(Convention.NameMustEndWith("Command"));
```

**Encapsulation** — built-in `Convention.PropertiesMustHavePrivateSetters` / `PropertiesMustHavePublicGetters`:
```csharp
entityTypes.MustConformTo(Convention.PropertiesMustHavePrivateSetters);
dtoTypes.MustConformTo(Convention.PropertiesMustHavePublicGetters);
```

**CQRS Data Access Separation** — built-in `Convention.MustNotTakeADependencyOn`:
```csharp
// Commands must use ApplicationDbContext (EF Core), not IDbConnection (Dapper)
commandHandlers.MustConformTo(Convention.MustNotTakeADependencyOn(typeof(IDbConnection), "Commands should use ApplicationDbContext"));
// Queries must use IDbConnection (Dapper), not ApplicationDbContext (EF Core)
queryHandlers.MustConformTo(Convention.MustNotTakeADependencyOn(typeof(ApplicationDbContext), "Queries should use IDbConnection"));
```

**Domain Model Integrity** — built-in `Convention.MustHaveANonPublicDefaultConstructor` + custom `ConventionSpecification`:
```csharp
// EF Core requires non-public parameterless constructors on entities
entityTypes.MustConformTo(Convention.MustHaveANonPublicDefaultConstructor);
// Value objects must have proper equality semantics
valueObjectTypes.MustConformTo(new MustOverrideEqualsAndGetHashCodeConvention());
```

**CQRS Handler Wiring** — custom `ConventionSpecification` (catches missing handlers at build time):
```csharp
// Every ICommand must have exactly one IRequestHandler<TCommand, TResponse>
commandTypes.MustConformTo(new MustHaveCorrespondingHandlerConvention(allHandlerTypes));
```

**CQRS Dual Interface Enforcement** — custom `ConventionSpecification`:
```csharp
// Commands must implement both ICommand (marker) AND IRequest<T>/IRequest (mediator dispatch)
commandTypes.MustConformTo(new MustImplementRequestInterfaceConvention());
```

#### Adding New Conventions

For checks covered by Best.Conventional built-ins (naming, properties, dependencies, constructors), use `Convention.*` directly. For structural/wiring checks, extend `ConventionSpecification`:

```csharp
private class MyCustomConvention : ConventionSpecification
{
    protected override string FailureMessage => "description of what's expected";

    public override ConventionResult IsSatisfiedBy(Type type)
    {
        return /* check passes */
            ? ConventionResult.Satisfied(type.FullName!)
            : ConventionResult.NotSatisfied(type.FullName!, "specific failure reason");
    }
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

### Minimal API Patterns

This project uses .NET 10 Minimal APIs with an endpoint definition pattern for better organization and maintainability.

#### **Endpoint Definition Pattern**

```csharp
public interface IEndpointDefinition
{
    void DefineEndpoints(WebApplication app);
}

public class CustomerEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var customers = app.MapGroup("/api/customers")
            .WithTags("Customers")
            .WithDescription("Customer management operations including CRUD functionality");

        customers.MapPost("/", CreateCustomer)
            .WithName("CreateCustomer")
            .WithDescription("Create a new customer")
            .Produces<CustomerDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        customers.MapGet("/{id}", GetCustomer)
            .WithName("GetCustomer")  
            .WithDescription("Get customer by ID")
            .Produces<CustomerReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateCustomer(
        CreateCustomerCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Created($"/api/customers/{result.Id}", result);
    }

    private static async Task<IResult> GetCustomer(
        int id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetCustomerQuery { Id = id };
        var result = await mediator.SendAsync(query, cancellationToken);
        return Results.Ok(result);
    }
}
```

#### **Auto-Discovery Extensions**

```csharp
public static class EndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var endpointDefinitions = typeof(Program).Assembly
            .GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IEndpointDefinition)) 
                       && !t.IsAbstract 
                       && !t.IsInterface)
            .Select(Activator.CreateInstance)
            .Cast<IEndpointDefinition>();

        foreach (var endpointDefinition in endpointDefinitions)
        {
            endpointDefinition.DefineEndpoints(app);
        }

        return app;
    }
}
```

#### **Benefits Over Controllers**
- **Performance**: ~30% faster than controller-based APIs
- **Less Boilerplate**: No inheritance, attributes, or base classes needed
- **Source Generators**: Better AOT compilation support
- **Flexible Filters**: Endpoint-specific middleware and behaviors
- **Modern .NET 10**: Native integration with latest framework features

#### **Filters vs Middleware: When to Use What**

**Use Middleware** for cross-cutting concerns that apply to **all or most** requests:
- Request logging (use `app.UseSerilogRequestLogging()` from Serilog)
- Authentication/Authorization (`app.UseAuthentication()`, `app.UseAuthorization()`)
- Error handling (`app.UseExceptionHandler()`)
- CORS (`app.UseCors()`)
- Response compression
- Security headers

**Use Endpoint Filters** for logic specific to **certain endpoints or groups**:
- Endpoint-specific validation rules
- Custom authorization rules for specific routes
- Request/response transformation for specific endpoints
- Caching behavior that varies by endpoint

**Why it matters**:
- **Middleware** runs once per request in the pipeline (more efficient for global concerns)
- **Endpoint Filters** run only for matched endpoints (better for endpoint-specific logic)
- **Middleware** runs earlier in the pipeline (before routing)
- **Endpoint Filters** run after routing and parameter binding

**Example - Request Logging**:
```csharp
// ✅ CORRECT - Use Serilog middleware for ALL request logging
app.UseSerilogRequestLogging();

// ❌ WRONG - Don't create endpoint filters for global concerns
// This would require adding the filter to every endpoint group
var orders = app.MapGroup("/api/orders")
    .AddEndpointFilter<LoggingFilter>();  // Inefficient and repetitive
```

**Example - Endpoint-Specific Filter**:
```csharp
// ✅ CORRECT - Use filter for endpoint-specific validation
public class ValidateOrderStatusFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
                                                 EndpointFilterDelegate next)
    {
        var status = context.GetArgument<string>(0);
        if (!Enum.TryParse<OrderStatus>(status, out _))
            return Results.BadRequest("Invalid order status");

        return await next(context);
    }
}

// Apply only to specific endpoints
orders.MapGet("/status/{status}", GetOrdersByStatus)
    .AddEndpointFilter<ValidateOrderStatusFilter>();
```

### Error Handling

**RFC 7807 Problem Details** with .NET 10 StatusCodeSelector:

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
- All tests must pass (204+ tests in current implementation)
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
- **Keep subsidiary docs in sync** - When changing framework versions, packages, patterns, or tooling (e.g. Swagger→Scalar, .NET version bumps, removing repository pattern), also update: `README.md`, `ASPIRE_SETUP_COMPLETE.md`, `docs/API-ENDPOINTS.md`, `docs/01-dotnet-setup/`, `docs/03-docker-setup/`, `docs/04-azure-deployment/`, `docs/05-aspire-setup/`

### Debugging Workflow

When encountering bugs or errors, **ALWAYS** follow this structured approach:

#### **Step 1: Reproduce the Bug**
- **Write a failing test first** that reproduces the issue (TDD approach)
- The test should fail for the right reason (exposing the bug)
- Run existing tests to see if they already catch the problem
- If working with runtime errors, start the application and trigger the error
- **Document the exact steps** to reproduce

#### **Step 2: Gather Complete Error Information**
- Collect the **full exception stack trace** (not just the first line)
- Identify the **exception type** (e.g., `DbUpdateException`, `ArgumentException`)
- Find the **actual error message** and any **inner exceptions**
- Check application logs for additional context

**Example**: For database errors, you need:
```
Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving...
 ---> System.ArgumentException: Parameter value '10.0000' is out of range.
   at Microsoft.Data.SqlClient.SqlCommand...
```

#### **Step 3: Analyze Root Cause**
- Read the error message carefully - it often tells you exactly what's wrong
- Check database constraints (column types, precision, null constraints)
- Verify domain validation rules match database schema
- Look for data type mismatches (e.g., percentage as `10.0` vs `0.10`)

**Common Issues**:
- `DECIMAL(5,4)` max value is `9.9999` - can't store `10.0`
- Percentage rates: Use decimal format (`0.10` for 10%), not whole numbers
- String length violations: Check `NVARCHAR(N)` limits
- Null constraint violations: Ensure required fields are set

#### **Step 4: Fix with Validation**
- Add domain-level validation to catch errors early
- Provide **clear, helpful error messages** that explain:
  - What the valid range/format is
  - Why the constraint exists (reference database schema)
  - How to fix the issue

**Example**:
```csharp
if (gstRate > 1.0m)
    throw new ArgumentOutOfRangeException(nameof(gstRate), gstRate,
        "GST rate must be a decimal value between 0 and 1 (e.g., 0.10 for 10%). " +
        "Database constraint: DECIMAL(5,4) with max value 9.9999.");
```

#### **Step 5: Add Tests**
- Write tests that verify the validation works correctly
- Test boundary conditions (edge cases)
- Test both valid and invalid inputs
- Ensure tests document expected behavior

**Example**:
```csharp
[Theory]
[InlineData(1.1)]
[InlineData(10.0)]  // Common mistake - using percentage as whole number
public void Constructor_WithGstRateGreaterThanOne_ShouldThrowException(decimal rate)
{
    var exception = Assert.Throws<ArgumentOutOfRangeException>(...);
    Assert.Contains("GST rate must be a decimal value between 0 and 1", exception.Message);
}
```

#### **Step 6: Verify the Fix**
- Run all tests to ensure nothing broke: `dotnet test`
- Test the original reproduction case manually
- Verify the error message is clear and helpful
- Update test count in documentation if needed

#### **Anti-Patterns to Avoid**
- ❌ **Guessing at fixes** without understanding root cause
- ❌ **Partial error messages** - always get the full stack trace
- ❌ **Skipping tests** - untested fixes often break later
- ❌ **Silent failures** - add validation that gives clear feedback
- ❌ **Fixing symptoms** instead of root cause

### Docker & Deployment Notes

#### **.NET 10 Dockerfile Changes**
- .NET 10 base images use **Ubuntu** (not Debian) — affects package repo setup
- Use modern GPG key management: `gpg --dearmor` + `signed-by=` in sources list (not deprecated `apt-key add`)
- Microsoft package repo path: `ubuntu/24.04/prod.list` (not `debian/11/prod.list`)

#### **Health Endpoint Mapping**
- `app.MapHealthChecks("/health")` must be mapped **unconditionally** in Program.cs for Docker healthcheck
- Aspire's `MapDefaultEndpoints()` only maps health checks in Development environment — insufficient for Docker

### Dev Tunnels

**What**: Expose your local API to the internet for webhook testing, mobile app development, or sharing with teammates. Uses Microsoft's Dev Tunnels infrastructure via `Aspire.Hosting.DevTunnels`.

**Prerequisites**: Install the Dev Tunnel CLI (see https://aka.ms/devtunnels/docs):
```bash
# macOS
brew install --cask devtunnel

# Windows
winget install Microsoft.devtunnel

# Linux
curl -sL https://aka.ms/DevTunnelCliInstall | bash

# Then authenticate (one-time, all platforms)
devtunnel user login
```

**Usage**: Dev tunnels are opt-in. Enable when starting the AppHost:
```bash
# Via command-line flag
dotnet run --project src/StarterApp.AppHost -- --devtunnel

# Via environment variable
ENABLE_DEV_TUNNEL=true dotnet run --project src/StarterApp.AppHost
```

When enabled, the Aspire dashboard shows the tunnel URL alongside the local API endpoint. The tunnel URL is publicly accessible (authenticated by default — use `.WithAnonymousAccess()` in Program.cs if you need unauthenticated webhook access).

### Local CI with nektos/act

**What**: Run GitHub Actions workflows locally using Docker before pushing. Tests your CI pipeline without commit-push-wait cycles.

**Prerequisites** (see https://nektosact.com/installation/):
```bash
# macOS
brew install act

# Windows
winget install nektos.act

# Linux
curl -s https://raw.githubusercontent.com/nektos/act/master/install.sh | sudo bash

# Requires Docker running (Docker Desktop, Colima, etc.)
```

**Usage** (`.actrc` pre-configures architecture and image — just run `act`):
```bash
# Run the CI workflow locally (from repo root)
act

# Run specific job
act -j build

# Run with verbose output for debugging
act -v

# List available workflows
act --list
```

**Existing CI workflow** (`.github/workflows/ci.yml`): Restores, builds (Release), and runs non-integration tests on .NET 10.

**Configuration files**: `.actrc` sets `--container-architecture linux/amd64` and the runner image. `.act.env` sets the container `PATH` so the `actions/setup-dotnet@v4` post-step can find Node.js (act's `docker exec` for post-steps doesn't inherit the container's `/etc/environment`).

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

# Run with dev tunnel (exposes API to internet)
dotnet run --project src/StarterApp.AppHost -- --devtunnel

# Run CI locally (flags pre-configured in .actrc)
act
```

This template ensures consistency, maintainability, and scalability while following .NET community best practices and modern architectural patterns.
