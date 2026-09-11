# Mediator Interfaces and Marker Pairings

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
public interface IOwnerAuthorizedMutation { }                  // marks commands that touch an existing aggregate; OwnerAuthorizationWriteGuard refuses their writes until Authorize runs
```

The pairings that follow from this:

- `ICommand` is a bare marker, so commands must **additionally** implement `IRequest<T>` explicitly:
  `class CreateCustomerCommand : ICommand, IRequest<CustomerDto>`
- `IQuery<TResult>` already extends `IRequest<TResult>`, so a query declares only `IQuery<T>`:
  `class GetCustomerQuery(int id) : IQuery<CustomerReadModel?>, ICacheable, IOwnerScopedRequest`
- Commands with no natural result use `IRequest<Unit>`. There is no non-generic `IRequest`.
- A cacheable by-id query returns a **nullable** read model — a miss is `null`, not a thrown `EntityNotFoundException`.

Convention tests enforce the pairings in both directions: `Commands_MustImplementBothICommandAndIRequest` (and the reverse — requests in a `Commands` namespace must be `ICommand`) and `RequestsInQueryNamespace_MustImplementIQuery`.

Note on constructor-set properties: `GetCustomerQuery` has a read-only `Id` set via constructor — use the constructor, not an object initializer.

How dispatch itself works (the cached typed-wrapper mechanism, the feature-toggle gate, pipeline behavior ordering) is documented in the `technology-stack` skill.
