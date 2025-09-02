namespace StarterApp.Api.Endpoints;

/// <summary>
/// Defines API endpoints for product management operations.
/// Provides CRUD operations for products including create, read, update, and delete functionality.
/// </summary>
public class ProductEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var products = app.MapGroup("/api/products")
            .WithTags("Products")
            .WithOpenApi();

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

    /// <summary>
    /// Gets all products from the catalog.
    /// </summary>
    private static async Task<IResult> GetProducts(IMediator mediator)
    {
        Log.Information("Getting all products");
        var query = new GetAllProductsQuery();
        var result = await mediator.SendAsync(query);
        return Results.Ok(result);
    }

    /// <summary>
    /// Gets a specific product by its ID.
    /// </summary>
    private static async Task<IResult> GetProduct(int id, IMediator mediator)
    {
        Log.Information("Getting product with ID: {Id}", id);
        var query = new GetProductByIdQuery(id);
        var result = await mediator.SendAsync(query);

        if (result == null)
        {
            Log.Warning("Product with ID: {Id} not found", id);
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    /// <summary>
    /// Creates a new product in the catalog.
    /// </summary>
    private static async Task<IResult> CreateProduct(CreateProductCommand command, IMediator mediator)
    {
        Log.Information("Creating a new product");
        var result = await mediator.SendAsync(command);

        Log.Information("Created new product with ID: {Id}", result.Id);
        return Results.Created($"/api/products/{result.Id}", result);
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    private static async Task<IResult> UpdateProduct(int id, UpdateProductCommand command, IMediator mediator)
    {
        if (id != command.Id)
        {
            return Results.BadRequest("ID in URL does not match ID in request body");
        }

        Log.Information("Updating product with ID: {Id}", id);

        try
        {
            await mediator.SendAsync(command);
            Log.Information("Updated product with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Product with ID: {Id} not found during update", id);
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Deletes a product by its ID.
    /// </summary>
    private static async Task<IResult> DeleteProduct(int id, IMediator mediator)
    {
        Log.Information("Deleting product with ID: {Id}", id);

        try
        {
            await mediator.SendAsync(new DeleteProductCommand(id));
            Log.Information("Deleted product with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Product with ID: {Id} not found during delete", id);
            return Results.NotFound();
        }

        return Results.NoContent();
    }
}
