namespace StarterApp.Api.Endpoints;

/// <summary>
/// Defines API endpoints for order management operations.
/// Provides functionality for creating orders, retrieving order details, updating status, and cancelling orders.
/// </summary>
public class OrderEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var orders = app.MapGroup("/api/v1/orders")
            .WithTags("Orders")
;

        orders.MapGet("/{id:int}", GetOrder)
            .WithName("GetOrder")
            .WithSummary("Get order by ID")
            .WithDescription("Retrieves a specific order with all its items by the order ID")
            .Produces<OrderWithItemsReadModel>(200, "application/json")
            .ProducesProblem(404)
            .ProducesProblem(500);

        orders.MapGet("/customer/{customerId:int}", GetOrdersByCustomer)
            .WithName("GetOrdersByCustomer")
            .WithSummary("Get orders by customer ID")
            .WithDescription("Retrieves all orders for a specific customer")
            .Produces<PagedResponse<OrderReadModel>>(200, "application/json")
            .ProducesProblem(500);

        orders.MapGet("/status/{status}", GetOrdersByStatus)
            .WithName("GetOrdersByStatus")
            .WithSummary("Get orders by status")
            .WithDescription("Retrieves all orders with the specified status")
            .Produces<PagedResponse<OrderReadModel>>(200, "application/json")
            .ProducesProblem(500);

        orders.MapPost("/", CreateOrder)
            .WithName("CreateOrder")
            .WithSummary("Create a new order")
            .WithDescription("Creates a new order with the specified items for a customer")
            .Accepts<CreateOrderCommand>("application/json")
            .Produces<OrderDto>(201, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(500);

        orders.MapPut("/{id:int}/status", UpdateOrderStatus)
            .WithName("UpdateOrderStatus")
            .WithSummary("Update order status")
            .WithDescription("Updates the status of an existing order")
            .Accepts<UpdateOrderStatusCommand>("application/json")
            .Produces<OrderDto>(200, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        orders.MapPost("/{id:int}/cancel", CancelOrder)
            .WithName("CancelOrder")
            .WithSummary("Cancel an order")
            .WithDescription("Cancels an existing order if it's in a cancellable state")
            .Produces<OrderDto>(200, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);
    }

    private static async Task<IResult> GetOrder(int id, IMediator mediator)
    {
        var query = new GetOrderByIdQuery { Id = id };
        var result = await mediator.SendAsync(query);

        if (result == null)
        {
            Log.Warning("Order with ID: {Id} not found", id);
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GetOrdersByCustomer(int customerId, IMediator mediator, int page = 1, int pageSize = 50)
    {
        var query = new GetOrdersByCustomerQuery { CustomerId = customerId, Page = page, PageSize = pageSize };
        var items = (await mediator.SendAsync(query)).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return Results.Ok(new PagedResponse<OrderReadModel> { Data = items, HasMore = hasMore });
    }

    private static async Task<IResult> GetOrdersByStatus(string status, IMediator mediator, int page = 1, int pageSize = 50)
    {
        var query = new GetOrdersByStatusQuery { Status = status, Page = page, PageSize = pageSize };
        var items = (await mediator.SendAsync(query)).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return Results.Ok(new PagedResponse<OrderReadModel> { Data = items, HasMore = hasMore });
    }

    private static async Task<IResult> CreateOrder(CreateOrderCommand command, IMediator mediator)
    {
        var result = await mediator.SendAsync(command);
        return Results.Created($"/api/v1/orders/{result.Id}", result);
    }

    private static async Task<IResult> UpdateOrderStatus(int id, UpdateOrderStatusCommand command, IMediator mediator)
    {
        if (id != command.OrderId)
        {
            return Results.BadRequest("ID in URL does not match ID in request body");
        }

        var result = await mediator.SendAsync(command);
        return Results.Ok(result);
    }

    private static async Task<IResult> CancelOrder(int id, IMediator mediator)
    {
        var command = new CancelOrderCommand { OrderId = id };
        var result = await mediator.SendAsync(command);
        return Results.Ok(result);
    }
}
