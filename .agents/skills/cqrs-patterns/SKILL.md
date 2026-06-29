---
name: cqrs-patterns
description: CQRS implementation — commands use EF Core DbContext, queries use Dapper IDbConnection. Use when implementing or modifying command/query handlers.
user-invocable: false
---

# CQRS Implementation Patterns

## Core CQRS Separation

- **Commands → DTOs**: Write operations return DTOs for client communication
- **Queries → ReadModels**: Read operations return ReadModels optimized for display
- **Never mix**: Don't return DTOs from queries or ReadModels from commands

## Interface Definitions

There is a SINGLE dispatch path. Every request is `IRequest<TResponse>` and every handler implements `IRequestHandler<TRequest, TResponse>` (`src/StarterApp.Api/Infrastructure/Mediator/IMediator.cs`). There is **no** `ICommandHandler` and **no** `IQueryHandler` — convention tests discover handlers via `IRequestHandler<,>`.

```csharp
// src/StarterApp.Api/Infrastructure/Mediator/IMediator.cs
public interface IRequest<out TResponse> { }

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

// src/StarterApp.Api/Application/Interfaces/ICQRSInterfaces.cs
public interface ICommand { }                                  // bare marker
public interface IQuery<TResult> : IRequest<TResult> { }       // extends IRequest — dispatchability is a compiler guarantee
public interface IOwnerScopedRequest { }                       // marks owner-scoped reads (and the cache-key seam)
public interface IOwnerAuthorizedMutation { }                  // marks non-create commands for the OwnerAuthorizationBehavior check
```

- `ICommand` is a bare marker, so commands must **additionally** implement `IRequest<T>` explicitly (e.g. `class CreateCustomerCommand : ICommand, IRequest<CustomerDto>`).
- `IQuery<TResult>` already extends `IRequest<TResult>`, so a query only declares `IQuery<T>`.
- Commands with no natural result use `IRequest<Unit>` — there is no non-generic `IRequest`.

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

**Commands use `ApplicationDbContext` directly** (`src/StarterApp.Api/Data/ApplicationDbContext.cs`) — no repository abstraction:
- DbContext already implements Unit of Work and Repository patterns internally
- A repository layer added indirection without value
- Direct DbContext usage is simpler, more explicit, and easier to debug
- Load entities as **tracked** (no `AsNoTracking`) so EF detects only changed properties
- Use `.Include()` to load navigations (e.g., `Orders.Include(o => o.Items)`)
- Mutate through domain methods, then single `SaveChangesAsync(cancellationToken)`
- **Never** use `AsNoTracking` + `Reconstitute` + `Update` in handlers — it marks all columns modified and creates lost-update risks (`Order.Reconstitute(...)` exists for read-only rehydration, not for the write path)
- A convention test (`CommandHandlers_MustDependOnApplicationDbContext`) requires every command handler to inject `ApplicationDbContext`; another (`CommandHandlers_MustNotDependOnIDbConnection`) forbids reaching for Dapper.

**Queries use Dapper** for optimized reads directly against SQL, returning lightweight ReadModels. Convention tests forbid `SELECT *` (`QueryHandlers_MustNotUseSelectStar` — list columns explicitly) and require every Dapper read to be wrapped in `PostgresRetryPolicy.ExecuteAsync` (`QueryHandlers_MustUsePostgresRetryPolicy`). `QueryHandlers_MustNotDependOnDbContext` forbids reaching for `ApplicationDbContext`.

## Owner authorization (convention-enforced)

Both command and query handlers MUST inject `IOwnerOnlyPolicy` AND actually invoke it:
- `CommandHandlers_MustInjectOwnerOnlyPolicy` / `QueryHandlers_MustInjectOwnerOnlyPolicy` check the constructor.
- `CommandHandlers_MustInvokeOwnerOnlyPolicy` / `QueryHandlers_MustInvokeOwnerOnlyPolicy` IL-scan (including async state machines) for an actual call to `GetRequiredScope()`/`Authorize(...)` — injecting without invoking fails.
- Queries that read an owner-scoped table must also implement `IOwnerScopedRequest` (`ResourceQueries_MustBeOwnerScoped`) AND filter EVERY owned-table `SELECT` literal by `owner_subject = @OwnerSubject AND tenant_id = @TenantId` (`OwnerScopedQueryHandlers_MustFilterSqlByOwnerScope`).
- Non-create commands additionally implement `IOwnerAuthorizedMutation` so `OwnerAuthorizationBehavior` can verify `IOwnerOnlyPolicy.Authorize` ran (creates are exempt — they stamp ownership instead of checking it).

## Command Handler Example

```csharp
public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public CreateCustomerCommandHandler(
        ApplicationDbContext dbContext,
        ICacheInvalidator cacheInvalidator,
        IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _dbContext = dbContext;
        _cacheInvalidator = cacheInvalidator;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<CustomerDto> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);

        // MUST invoke IOwnerOnlyPolicy — injection alone fails the convention test.
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        // Ownership is stamped into the aggregate via the real ctor:
        // Customer(string name, Email email, string ownerSubject, string tenantId)
        var customer = new Customer(command.Name, email, ownerScope.OwnerSubject, ownerScope.TenantId);

        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Write handlers invalidate the affected cache entry after the commit.
        await _cacheInvalidator.InvalidateCustomerAsync(customer.Id, cancellationToken);

        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email.Value,
            DateCreated = customer.DateCreated,
            IsActive = customer.IsActive
        };
    }
}
```

(The real `CreateCustomerCommandHandler` adds an `EnableRetryOnFailure` execution-strategy loop with email-uniqueness 409-as-dedup; the shape above is the minimal convention-passing skeleton — inject + invoke `IOwnerOnlyPolicy`, stamp ownership, single `SaveChangesAsync`, invalidate cache.)

## Query Handler Example

```csharp
public class GetCustomerQueryHandler : IRequestHandler<GetCustomerQuery, CustomerReadModel?>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetCustomerQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<CustomerReadModel?> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        // No SELECT * — list columns. Owner predicates on every owned-table SELECT literal.
        var sql = @"
            SELECT
                id AS ""Id"",
                name AS ""Name"",
                email AS ""Email"",
                date_created AS ""DateCreated"",
                is_active AS ""IsActive""
            FROM customers
            WHERE id = @Id
              AND owner_subject = @OwnerSubject
              AND tenant_id = @TenantId";

        // Dapper reads go through PostgresRetryPolicy.ExecuteAsync. Return null on miss — do not throw.
        return await PostgresRetryPolicy.ExecuteAsync(
            ct => _connection.QueryFirstOrDefaultAsync<CustomerReadModel>(
                new CommandDefinition(sql,
                    new { query.Id, ownerScope.OwnerSubject, ownerScope.TenantId },
                    cancellationToken: ct)),
            cancellationToken);
    }
}
```

`GetCustomerQuery` is `IQuery<CustomerReadModel?>, ICacheable, IOwnerScopedRequest` and returns a **nullable** read model — a miss returns `null`, not a thrown `KeyNotFoundException`.

## Validator Coverage

Every command AND every query must have an `IValidator<T>` — even trivial ones (`EveryCommand_MustHaveAValidator`, `EveryQuery_MustHaveAValidator`). This is a deliberate AI-agent-maintenance rule: it removes the judgment call about which requests "need" validation.

## Auto-Registration

```csharp
// In Program.cs - Single line registers ALL handlers
builder.Services.AddMediator(Assembly.GetExecutingAssembly());

// Auto-discovers and registers:
// - Every command/query handler implementing IRequestHandler<,>
// - Zero manual registration needed
```

## Dual Interface Requirement

Commands must implement both `ICommand` (marker) AND `IRequest<T>` (mediator dispatch); commands with no result use `IRequest<Unit>`. Queries declare `IQuery<T>`, which already extends `IRequest<T>`. Convention tests enforce this: `Commands_MustImplementBothICommandAndIRequest` (and the reverse — requests in the `Commands` namespace must be `ICommand`) and `RequestsInQueryNamespace_MustImplementIQuery`.
