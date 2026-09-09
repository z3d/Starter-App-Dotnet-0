namespace StarterApp.Api.Endpoints;

public class ProductEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var products = app.MapGroup("/api/v1/products")
            .WithTags("Products")
            .RequireAuthorization();

        products.MapGet("/", GetProducts)
            .WithName("GetProducts")
            .WithSummary("Get all products")
            .RequireScope("products:read")
            .Produces<PagedResponse<ProductReadModel>>(200, "application/json")
            .ProducesProblem(500);

        products.MapGet("/{id:int}", GetProduct)
            .WithName("GetProduct")
            .WithSummary("Get product by ID")
            .RequireScope("products:read")
            .Produces<ProductReadModel>(200, "application/json")
            .ProducesProblem(404)
            .ProducesProblem(500);

        products.MapPost("/", CreateProduct)
            .WithName("CreateProduct")
            .WithSummary("Create a new product")
            .RequireScope("products:write")
            .SecuredBy2Fa()
            .Accepts<CreateProductCommand>("application/json")
            .Produces<ProductDto>(201, "application/json")
            .ProducesProblem(400)
            .ProducesProblem(500);

        products.MapPut("/{id:int}", UpdateProduct)
            .WithName("UpdateProduct")
            .WithSummary("Update an existing product")
            .RequireScope("products:write")
            .SecuredBy2Fa()
            .Accepts<UpdateProductCommand>("application/json")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        products.MapDelete("/{id:int}", DeleteProduct)
            .WithName("DeleteProduct")
            .WithSummary("Delete a product")
            .RequireScope("products:write")
            .SecuredBy2Fa()
            .Produces(204)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(500);
    }

    private static async Task<IResult> GetProducts(IMediator mediator, CancellationToken cancellationToken, int page = 1, int pageSize = 50)
    {
        var query = new GetAllProductsQuery { Page = page, PageSize = pageSize };
        return await mediator.PagedAsync(query, pageSize, cancellationToken);
    }

    private static async Task<IResult> GetProduct(int id, IMediator mediator, CancellationToken cancellationToken)
    {
        var query = new GetProductByIdQuery(id);
        var result = await mediator.SendAsync(query, cancellationToken);

        if (result == null)
        {
            Log.Warning("Product with ID: {Id} not found", id);
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateProduct(CreateProductCommand command, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Created($"/api/v1/products/{result.Id}", result);
    }

    private static async Task<IResult> UpdateProduct(int id, UpdateProductCommand command, IMediator mediator, CancellationToken cancellationToken)
    {
        if (id != command.Id)
        {
            return Results.BadRequest("ID in URL does not match ID in request body");
        }

        await mediator.SendAsync(command, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteProduct(int id, IMediator mediator, CancellationToken cancellationToken)
    {
        await mediator.SendAsync(new DeleteProductCommand(id), cancellationToken);
        return Results.NoContent();
    }
}
