namespace StarterApp.Api.Endpoints;

/// <summary>
/// Defines API endpoints for customer management operations.
/// Provides CRUD operations for customers including create, read, update, and delete functionality.
/// </summary>
public class CustomerEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var customers = app.MapGroup("/api/customers")
            .WithTags("Customers")
            .WithOpenApi();

        customers.MapGet("/", GetCustomers)
            .WithName("GetCustomers")
            .WithSummary("Get all customers")
            .WithDescription("Retrieves a list of all customers in the system")
            .Produces<IEnumerable<CustomerReadModel>>(200, "application/json")
            .ProducesProblem(500);

        customers.MapGet("/{id:int}", GetCustomer)
            .WithName("GetCustomer")
            .WithSummary("Get customer by ID")
            .WithDescription("Retrieves a specific customer by their unique identifier")
            .Produces<CustomerReadModel>(200, "application/json")
            .ProducesProblem(404)
            .ProducesProblem(500);

        customers.MapPost("/", CreateCustomer)
            .WithName("CreateCustomer")
            .WithSummary("Create a new customer")
            .WithDescription("Creates a new customer with the provided information")
            .Accepts<CreateCustomerCommand>("application/json")
            .Produces<CustomerDto>(201, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(500);

        customers.MapPut("/{id:int}", UpdateCustomer)
            .WithName("UpdateCustomer")
            .WithSummary("Update an existing customer")
            .WithDescription("Updates an existing customer with the provided information")
            .Accepts<UpdateCustomerCommand>("application/json")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        customers.MapDelete("/{id:int}", DeleteCustomer)
            .WithName("DeleteCustomer")
            .WithSummary("Delete a customer")
            .WithDescription("Permanently deletes a customer from the system")
            .Produces(204)
            .ProducesProblem(404)
            .ProducesProblem(500);
    }

    /// <summary>
    /// Gets all customers from the system.
    /// </summary>
    private static async Task<IResult> GetCustomers(IMediator mediator)
    {
        Log.Information("Getting all customers");
        var query = new GetCustomersQuery();
        var result = await mediator.SendAsync(query);
        return Results.Ok(result);
    }

    /// <summary>
    /// Gets a specific customer by their ID.
    /// </summary>
    private static async Task<IResult> GetCustomer(int id, IMediator mediator)
    {
        Log.Information("Getting customer with ID: {Id}", id);
        var query = new GetCustomerQuery(id);
        var result = await mediator.SendAsync(query);

        if (result == null)
        {
            Log.Warning("Customer with ID: {Id} not found", id);
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    /// <summary>
    /// Creates a new customer.
    /// </summary>
    private static async Task<IResult> CreateCustomer(CreateCustomerCommand command, IMediator mediator)
    {
        Log.Information("Creating a new customer");
        var result = await mediator.SendAsync(command);

        Log.Information("Created new customer with ID: {Id}", result.Id);
        return Results.Created($"/api/customers/{result.Id}", result);
    }

    /// <summary>
    /// Updates an existing customer.
    /// </summary>
    private static async Task<IResult> UpdateCustomer(int id, UpdateCustomerCommand command, IMediator mediator)
    {
        if (id != command.Id)
        {
            return Results.BadRequest("ID in URL does not match ID in request body");
        }

        Log.Information("Updating customer with ID: {Id}", id);

        try
        {
            await mediator.SendAsync(command);
            Log.Information("Updated customer with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Customer with ID: {Id} not found during update", id);
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Deletes a customer by their ID.
    /// </summary>
    private static async Task<IResult> DeleteCustomer(int id, IMediator mediator)
    {
        Log.Information("Deleting customer with ID: {Id}", id);

        try
        {
            await mediator.SendAsync(new DeleteCustomerCommand { Id = id });
            Log.Information("Deleted customer with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Customer with ID: {Id} not found during delete", id);
            return Results.NotFound();
        }

        return Results.NoContent();
    }
}
