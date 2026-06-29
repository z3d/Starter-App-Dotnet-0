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
        // Every /api/v1 group MUST call RequireGatewayIdentity(); every route MUST declare
        // RequireScope("domain:read|write"); every non-GET route MUST also call SecuredBy2Fa().
        // ApiConventionTests enforces all three from the mapped endpoint metadata.
        var customers = app.MapGroup("/api/v1/customers")
            .WithTags("Customers")
            .RequireGatewayIdentity();

        customers.MapGet("/{id:int}", GetCustomer)
            .WithName("GetCustomer")
            .WithSummary("Get customer by ID")
            .WithDescription("Retrieves a specific customer by their unique identifier")
            .RequireScope("customers:read")
            .Produces<CustomerReadModel>(200, "application/json")
            .ProducesProblem(404)
            .ProducesProblem(500);

        customers.MapPost("/", CreateCustomer)
            .WithName("CreateCustomer")
            .WithSummary("Create a new customer")
            .WithDescription("Creates a new customer with the provided information")
            .RequireScope("customers:write")
            .SecuredBy2Fa()
            .Accepts<CreateCustomerCommand>("application/json")
            .Produces<CustomerDto>(201, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(500);
    }

    // Handlers MUST bind a CancellationToken and forward it to mediator.SendAsync —
    // ApiConventionTests.ApiRouteEndpoints_MustBindACancellationToken enforces this.
    private static async Task<IResult> CreateCustomer(
        CreateCustomerCommand command, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Created($"/api/v1/customers/{result.Id}", result);
    }

    private static async Task<IResult> GetCustomer(
        int id, IMediator mediator, CancellationToken cancellationToken)
    {
        // GetCustomerQuery has a read-only Id set via constructor — use the ctor, not an object initializer.
        var query = new GetCustomerQuery(id);
        var result = await mediator.SendAsync(query, cancellationToken);

        if (result == null)
            return Results.NotFound();

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
        var endpointDefinitions = typeof(IApiMarker).Assembly
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