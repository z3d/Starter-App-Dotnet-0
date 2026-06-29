---
name: technology-stack
description: Core dependencies and versions, custom mediator implementation. Use when adding dependencies or checking version compatibility.
user-invocable: false
---

# Technology Stack

## Core Dependencies

### Framework & Hosting
- **.NET 10.0**: Latest framework
- **Aspire.Hosting.AppHost** (13.2.3+): Service orchestration
- **Aspire.Hosting.PostgreSQL** (13.2.3+): Database container management
- **Aspire.Hosting.Seq** (13.2.3+): Structured logging
- **Aspire.Hosting.DevTunnels** (13.2.3+): Expose local services to internet

### Data Access
- **Entity Framework Core 10.0.8+**: Write operations
- **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2+**: EF Core PostgreSQL provider
- **Npgsql 10.0.3+**: PostgreSQL connectivity
- **Dapper 2.1.35+**: Optimized read operations

### Logging & Observability
- **Serilog.AspNetCore 10.0.0+**: Structured logging
- **OpenTelemetry**: Metrics, tracing, telemetry
- **Aspire Dashboard**: Development-time observability

### API & Documentation
- **Microsoft.AspNetCore.OpenApi 10.0.3+**: Native .NET 10 OpenAPI
- **Scalar.AspNetCore 2.11+**: API reference UI (at `/scalar/v1`)

### Testing
- **xUnit**: Primary testing framework
- **FsCheck 3.3.3 + FsCheck.Xunit 3.3.3**: Property-based (fuzz) testing (3.x — a major-version bump from 2.x with a different API; fuzz tests use the FsCheck 3.x surface, e.g. `Gen.OneOf`)
- **Testcontainers.PostgreSql**: Database integration testing
- **Microsoft.AspNetCore.Mvc.Testing**: API integration testing
- **Moq**: Mocking framework
- **Best.Conventional**: Architectural rule enforcement

## Custom Mediator Implementation

**Custom CQRS Mediator** (replaces commercial MediatR). The public contract:

```csharp
public interface IMediator
{
    // Single dispatch path: every request is IRequest<TResponse>, so every command/query runs
    // through the same IPipelineBehavior chain. Commands with no natural result return Unit.
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IRequest<out TResponse> { }

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
```

The implementation (`StarterApp.Api/Infrastructure/Mediator/Mediator.cs`) deliberately avoids
`MethodInfo.Invoke`. It runs a feature-toggle gate and validators, then dispatches through a
strongly-typed wrapper that is **built once per request type and cached** for the process lifetime
(`ConcurrentDictionary<Type, object>`). Every subsequent `SendAsync` is a dictionary lookup plus a
strongly-typed virtual call — no per-call `MethodInfo.Invoke`, no `object[]` argument allocation:

```csharp
public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(request);

    RunFeatureToggleGate(request);  // [FeatureToggle("name")] → FeatureDisabledException (503) before anything else
    RunValidators(request);         // every command/query has a validator (convention-enforced)

    var wrapper = (RequestHandlerWrapper<TResponse>)RequestHandlerWrappers.GetOrAdd(
        request.GetType(),
        static (requestType, responseType) =>
            Activator.CreateInstance(typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType))!,
        typeof(TResponse));

    return wrapper.HandleAsync(request, _serviceProvider, cancellationToken);
}

// Inside RequestHandlerWrapperImpl<TRequest, TResponse>: resolves IRequestHandler<TRequest, TResponse>,
// wraps the call in the IPipelineBehavior<TRequest, TResponse> chain (registration order, first
// registered runs outermost), and invokes handler.HandleAsync(typed, cancellationToken) directly.
```

Benefits: No commercial licensing, simple transparent implementation, full control over dispatch.
The per-request-type wrapper is created once and cached, so dispatch carries no per-call reflection
cost; the feature-toggle gate, validators, and pipeline behaviors are all centralized in this one path.
