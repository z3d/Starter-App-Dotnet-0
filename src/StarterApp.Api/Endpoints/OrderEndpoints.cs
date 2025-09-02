namespace StarterApp.Api.Endpoints;

/// <summary>
/// Defines API endpoints for order management operations.
/// Provides functionality for creating orders, retrieving order details, updating status, and cancelling orders.
/// </summary>
public class OrderEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var orders = app.MapGroup("/api/orders")
            .WithTags("Orders")
            .WithOpenApi();

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
            .Produces<IEnumerable<OrderReadModel>>(200, "application/json")
            .ProducesProblem(500);

        orders.MapGet("/status/{status}", GetOrdersByStatus)
            .WithName("GetOrdersByStatus")
            .WithSummary("Get orders by status")
            .WithDescription("Retrieves all orders with the specified status")
            .Produces<IEnumerable<OrderReadModel>>(200, "application/json")
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

    /// <summary>
    /// Gets a specific order with all its items by ID.
    /// </summary>
    private static async Task<IResult> GetOrder(int id, IMediator mediator)
    {
        Log.Information("Getting order with ID: {Id}", id);
        var query = new GetOrderByIdQuery { Id = id };
        var result = await mediator.SendAsync(query);

        if (result == null)
        {
            Log.Warning("Order with ID: {Id} not found", id);
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    /// <summary>
    /// Gets all orders for a specific customer.
    /// </summary>
    private static async Task<IResult> GetOrdersByCustomer(int customerId, IMediator mediator)
    {
        Log.Information("Getting orders for customer: {CustomerId}", customerId);
        var query = new GetOrdersByCustomerQuery { CustomerId = customerId };
        var result = await mediator.SendAsync(query);
        return Results.Ok(result);
    }

    /// <summary>
    /// Gets all orders with a specific status.
    /// </summary>
    private static async Task<IResult> GetOrdersByStatus(string status, IMediator mediator)
    {
        Log.Information("Getting orders with status: {Status}", status);
        var query = new GetOrdersByStatusQuery { Status = status };
        var result = await mediator.SendAsync(query);
        return Results.Ok(result);
    }

    /// <summary>
    /// Creates a new order for a customer.
    /// </summary>
    private static async Task<IResult> CreateOrder(CreateOrderCommand command, IMediator mediator)
    {
        Log.Information("Creating a new order for customer: {CustomerId}", command.CustomerId);

        var result = await mediator.SendAsync(command);
        Log.Information("Created new order with ID: {Id}", result.Id);
        return Results.Created($"/api/orders/{result.Id}", result);
    }

    /// <summary>
    /// Updates the status of an existing order.
    /// </summary>
    private static async Task<IResult> UpdateOrderStatus(int id, UpdateOrderStatusCommand command, IMediator mediator)
    {
        if (id != command.OrderId)
        {
            return Results.BadRequest("ID in URL does not match ID in request body");
        }

        Log.Information("Updating order {Id} status to: {Status}", id, command.Status);

        try
        {
            var result = await mediator.SendAsync(command);
            Log.Information("Updated order {Id} status to: {Status}", id, command.Status);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            Log.Warning("Order with ID: {Id} not found during status update: {Message}", id, ex.Message);
            return Results.NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning("Invalid status transition for order {Id}: {Message}", id, ex.Message);
            return Results.BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            Log.Warning("Invalid status value for order {Id}: {Message}", id, ex.Message);
            return Results.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Cancels an existing order.
    /// </summary>
    private static async Task<IResult> CancelOrder(int id, IMediator mediator)
    {
        Log.Information("Cancelling order with ID: {Id}", id);

        try
        {
            var command = new CancelOrderCommand { OrderId = id };
            var result = await mediator.SendAsync(command);
            Log.Information("Cancelled order with ID: {Id}", id);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            Log.Warning("Order with ID: {Id} not found during cancellation: {Message}", id, ex.Message);
            return Results.NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning("Cannot cancel order {Id}: {Message}", id, ex.Message);
            return Results.BadRequest(ex.Message);
        }
    }
}
