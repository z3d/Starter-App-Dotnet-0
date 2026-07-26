# Endpoint Patterns

## The endpoint definition shape

```csharp
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
            .RequireScope("customers:read")
            .Produces<CustomerReadModel>(200, "application/json")
            .ProducesProblem(404)
            .ProducesProblem(500);

        customers.MapPost("/", CreateCustomer)
            .WithName("CreateCustomer")
            .WithSummary("Create a new customer")
            .RequireScope("customers:write")
            .SecuredBy2Fa()
            .Accepts<CreateCustomerCommand>("application/json")
            .Produces<CustomerDto>(201, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(500);
    }

    // Handlers MUST bind a CancellationToken and forward it —
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
        // GetCustomerQuery has a read-only Id set via constructor — use the ctor, not an initializer.
        var query = new GetCustomerQuery(id);
        var result = await mediator.SendAsync(query, cancellationToken);
        return result == null ? Results.NotFound() : Results.Ok(result);
    }
}
```

## Auto-discovery

`MapApiEndpoints()` reflects over the API assembly for concrete `IEndpointDefinition` implementations, instantiates each, and calls `DefineEndpoints(app)`. Adding an endpoint class is sufficient — there is no registration list to update.

## Filters vs middleware

Middleware runs once per request, before routing — efficient for global concerns: request logging, error handling, CORS, compression, security headers, payload capture, gateway identity.

Endpoint filters run only for matched endpoints, after routing and parameter binding — right for route-specific logic:

```csharp
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

## Problem Details setup

```csharp
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
```

Status mapping is owned by the closed table in `ExceptionHandlingMiddleware` / `ResolveExceptionStatusCode`: `ArgumentException` → 400, `EntityNotFoundException` → 404, `DomainRuleException` → 409, everything else — including bare `InvalidOperationException` / `KeyNotFoundException`, which are treated as server bugs — → 500. Rationale for that last mapping is in `docs/DECISIONS.md`.
