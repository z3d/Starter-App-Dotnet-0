namespace StarterApp.Api.Endpoints;

/// <summary>
/// Defines API endpoints for customer management operations.
/// Provides CRUD operations for customers including create, read, update, and delete functionality.
/// </summary>
public class CustomerEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var customers = app.MapGroup("/api/v1/customers")
            .WithTags("Customers")
;

        customers.MapGet("/", GetCustomers)
            .WithName("GetCustomers")
            .WithSummary("Get all customers")
            .WithDescription("Retrieves a list of all customers in the system")
            .Produces<PagedResponse<CustomerReadModel>>(200, "application/json")
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
            .ProducesProblem(409)
            .ProducesProblem(500);
    }

    private static async Task<IResult> GetCustomers(IMediator mediator, int page = 1, int pageSize = 50)
    {
        var query = new GetCustomersQuery { Page = page, PageSize = pageSize };
        var items = (await mediator.SendAsync(query)).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        return Results.Ok(new PagedResponse<CustomerReadModel> { Data = items, HasMore = hasMore });
    }

    private static async Task<IResult> GetCustomer(int id, IMediator mediator)
    {
        var query = new GetCustomerQuery(id);
        var result = await mediator.SendAsync(query);

        if (result == null)
        {
            Log.Warning("Customer with ID: {Id} not found", id);
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateCustomer(CreateCustomerCommand command, IMediator mediator)
    {
        var result = await mediator.SendAsync(command);
        return Results.Created($"/api/v1/customers/{result.Id}", result);
    }

    private static async Task<IResult> UpdateCustomer(int id, UpdateCustomerCommand command, IMediator mediator)
    {
        if (id != command.Id)
        {
            return Results.BadRequest("ID in URL does not match ID in request body");
        }

        await mediator.SendAsync(command);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCustomer(int id, IMediator mediator)
    {
        await mediator.SendAsync(new DeleteCustomerCommand { Id = id });
        return Results.NoContent();
    }
}
