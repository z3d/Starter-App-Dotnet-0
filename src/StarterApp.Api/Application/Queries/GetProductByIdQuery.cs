namespace StarterApp.Api.Application.Queries;

public class GetProductByIdQuery : IQuery<ProductReadModel?>, IRequest<ProductReadModel?>
{
    public int Id { get; }

    public GetProductByIdQuery(int id)
    {
        Id = id;
    }
}

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductReadModel?>
{
    private readonly IDbConnection _connection;

    public GetProductByIdQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<ProductReadModel?> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetProductByIdQuery for product {Id}", query.Id);

        var sqlQuery = @"
            SELECT
                Id,
                Name,
                Description,
                PriceAmount,
                PriceCurrency,
                Stock,
                LastUpdated
            FROM Products
            WHERE Id = @Id";

        var product = await _connection.QueryFirstOrDefaultAsync<ProductReadModel>(sqlQuery, new { Id = query.Id });

        if (product == null)
        {
            Log.Warning("Product with ID {Id} not found", query.Id);
            return null;
        }

        return product;
    }

    public async Task<ProductReadModel?> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}


