namespace StarterApp.Api.Endpoints;

/// <summary>
/// Defines API endpoints for product management operations.
/// Provides CRUD operations for products including create, read, update, and delete functionality.
/// </summary>
public class ProductEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var products = app.MapGroup("/api/v1/products")
            .WithTags("Products")
;

        products.MapGet("/", GetProducts)
            .WithName("GetProducts")
            .WithSummary("Get all products")
            .WithDescription("Retrieves a list of all products in the catalog")
            .Produces<IEnumerable<ProductReadModel>>(200, "application/json")
            .ProducesProblem(500);

        products.MapGet("/{id:int}", GetProduct)
            .WithName("GetProduct")
            .WithSummary("Get product by ID")
            .WithDescription("Retrieves a specific product by its unique identifier")
            .Produces<ProductReadModel>(200, "application/json")
            .ProducesProblem(404)
            .ProducesProblem(500);

        products.MapPost("/", CreateProduct)
            .WithName("CreateProduct")
            .WithSummary("Create a new product")
            .WithDescription("Creates a new product in the catalog with the provided information")
            .Accepts<CreateProductCommand>("application/json")
            .Produces<ProductDto>(201, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(500);

        products.MapPut("/{id:int}", UpdateProduct)
            .WithName("UpdateProduct")
            .WithSummary("Update an existing product")
            .WithDescription("Updates an existing product with the provided information")
            .Accepts<UpdateProductCommand>("application/json")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        products.MapDelete("/{id:int}", DeleteProduct)
            .WithName("DeleteProduct")
            .WithSummary("Delete a product")
            .WithDescription("Permanently removes a product from the catalog")
            .Produces(204)
            .ProducesProblem(404)
            .ProducesProblem(500);
    }

    private static async Task<IResult> GetProducts(HttpContext httpContext, IMediator mediator, int page = 1, int pageSize = 50)
    {
        var query = new GetAllProductsQuery { Page = page, PageSize = pageSize };
        var items = (await mediator.SendAsync(query)).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        httpContext.Response.Headers["X-Has-More"] = hasMore.ToString().ToLower();
        return Results.Ok(items);
    }

    private static async Task<IResult> GetProduct(int id, IMediator mediator)
    {
        var query = new GetProductByIdQuery(id);
        var result = await mediator.SendAsync(query);

        if (result == null)
        {
            Log.Warning("Product with ID: {Id} not found", id);
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateProduct(CreateProductCommand command, IMediator mediator)
    {
        var result = await mediator.SendAsync(command);
        return Results.Created($"/api/v1/products/{result.Id}", result);
    }

    private static async Task<IResult> UpdateProduct(int id, UpdateProductCommand command, IMediator mediator)
    {
        if (id != command.Id)
        {
            return Results.BadRequest("ID in URL does not match ID in request body");
        }

        await mediator.SendAsync(command);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteProduct(int id, IMediator mediator)
    {
        await mediator.SendAsync(new DeleteProductCommand(id));
        return Results.NoContent();
    }
}
