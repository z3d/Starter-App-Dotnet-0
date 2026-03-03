# CQRS Implementation Patterns

## Core CQRS Separation

- **Commands → DTOs**: Write operations return DTOs for client communication
- **Queries → ReadModels**: Read operations return ReadModels optimized for display
- **Never mix**: Don't return DTOs from queries or ReadModels from commands

## Interface Definitions

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

## Return Type Standards

```csharp
// CORRECT - Commands return DTOs
public async Task<ActionResult<CustomerDto>> CreateCustomer(CreateCustomerCommand command)
public async Task<ActionResult<ProductDto>> UpdateProduct(int id, UpdateProductCommand command)

// CORRECT - Queries return ReadModels
public async Task<ActionResult<CustomerReadModel>> GetCustomer(int id)
public async Task<ActionResult<IEnumerable<ProductReadModel>>> GetAllProducts()

// WRONG - Mixed concerns
// public async Task<ActionResult<CustomerDto>> GetCustomer(int id)      // Should be ReadModel
// public async Task<ActionResult<CustomerReadModel>> CreateCustomer()   // Should be DTO
```

## Data Access Design

**Commands use DbContext directly** — no repository abstraction:
- DbContext already implements Unit of Work and Repository patterns internally
- A repository layer added indirection without value
- Direct DbContext usage is simpler, more explicit, and easier to debug
- Entities loaded with `AsNoTracking()` then reconstituted via domain factory methods

**Queries use Dapper** for optimized reads directly against SQL, returning lightweight ReadModels.

## Command Handler Example

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

## Query Handler Example

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

## Auto-Registration

```csharp
// In Program.cs - Single line registers ALL handlers
builder.Services.AddMediator(Assembly.GetExecutingAssembly());

// Auto-discovers and registers:
// - All command handlers implementing IRequestHandler<,>
// - All query handlers implementing IRequestHandler<,>
// - Zero manual registration needed
```

## Dual Interface Requirement

Commands must implement both `ICommand` (marker) AND `IRequest<T>`/`IRequest` (mediator dispatch). Same for queries: `IQuery<T>` AND `IRequest<T>`. Convention tests enforce this bidirectionally.
