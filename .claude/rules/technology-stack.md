# Technology Stack

## Core Dependencies

### Framework & Hosting
- **.NET 10.0**: Latest framework
- **Aspire.Hosting.AppHost** (13.1.0+): Service orchestration
- **Aspire.Hosting.SqlServer** (13.1.0+): Database container management
- **Aspire.Hosting.Seq** (13.1.0+): Structured logging
- **Aspire.Hosting.DevTunnels** (13.1.0+): Expose local services to internet

### Data Access
- **Entity Framework Core 10.0.3+**: Write operations
- **Dapper 2.1.35+**: Optimized read operations
- **Microsoft.Data.SqlClient**: SQL Server connectivity

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
- **Testcontainers.MsSql**: Database integration testing
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
