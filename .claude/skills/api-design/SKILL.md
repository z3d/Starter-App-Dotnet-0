---
name: api-design
description: Minimal API endpoint patterns with IEndpointDefinition, filters, error handling. Use when creating or modifying API endpoints.
user-invocable: false
---

# API Design Standards

## Minimal API Patterns

This project uses .NET 10 Minimal APIs with an endpoint definition pattern.

### Endpoint Definition Pattern

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
            .WithDescription("Customer management operations");

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
        CreateCustomerCommand command, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Created($"/api/customers/{result.Id}", result);
    }

    private static async Task<IResult> GetCustomer(
        int id, IMediator mediator, CancellationToken cancellationToken)
    {
        var query = new GetCustomerQuery { Id = id };
        var result = await mediator.SendAsync(query, cancellationToken);
        return Results.Ok(result);
    }
}
```

### Auto-Discovery Extensions

```csharp
public static class EndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var endpointDefinitions = typeof(Program).Assembly
            .GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IEndpointDefinition))
                       && !t.IsAbstract && !t.IsInterface)
            .Select(Activator.CreateInstance)
            .Cast<IEndpointDefinition>();

        foreach (var endpointDefinition in endpointDefinitions)
            endpointDefinition.DefineEndpoints(app);

        return app;
    }
}
```

### Benefits Over Controllers
- ~30% faster than controller-based APIs
- Less boilerplate: no inheritance, attributes, or base classes
- Better AOT compilation support via source generators
- Flexible endpoint-specific filters
- Native .NET 10 integration

## Filters vs Middleware

**Use Middleware** for cross-cutting concerns on all/most requests:
- Request logging (`app.UseSerilogRequestLogging()`)
- Authentication/Authorization
- Error handling (`app.UseExceptionHandler()`)
- CORS, response compression, security headers

**Use Endpoint Filters** for logic specific to certain endpoints/groups:
- Endpoint-specific validation rules
- Custom authorization for specific routes
- Request/response transformation per endpoint
- Caching behavior that varies by endpoint

**Why it matters**:
- Middleware runs once per request, before routing (efficient for global concerns)
- Endpoint filters run only for matched endpoints, after routing and parameter binding

```csharp
// CORRECT - Use middleware for global concerns
app.UseSerilogRequestLogging();

// CORRECT - Use filter for endpoint-specific validation
public class ValidateOrderStatusFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var status = context.GetArgument<string>(0);
        if (!Enum.TryParse<OrderStatus>(status, out _))
            return Results.BadRequest("Invalid order status");
        return await next(context);
    }
}

orders.MapGet("/status/{status}", GetOrdersByStatus)
    .AddEndpointFilter<ValidateOrderStatusFilter>();
```

## Error Handling

**RFC 7807 Problem Details** with .NET 10 StatusCodeSelector:

```csharp
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = (context) =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

// Automatic status code mapping:
// ArgumentException → 400 Bad Request
// KeyNotFoundException → 404 Not Found
// Other exceptions → 500 Internal Server Error
```