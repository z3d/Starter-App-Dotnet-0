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
- **FsCheck 2.16.6 + FsCheck.Xunit**: Property-based (fuzz) testing
- **Testcontainers.PostgreSql**: Database integration testing
- **Microsoft.AspNetCore.Mvc.Testing**: API integration testing
- **Moq**: Mocking framework
- **Best.Conventional**: Architectural rule enforcement

## Custom Mediator Implementation

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

Benefits: No commercial licensing, simple transparent implementation, full control over dispatch, zero reflection overhead in handlers.
