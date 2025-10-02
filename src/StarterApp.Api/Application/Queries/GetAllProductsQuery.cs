namespace StarterApp.Api.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductReadModel>>, IRequest<IEnumerable<ProductReadModel>>
{
}

public class GetAllProductsQueryHandler : IRequestHandler<GetAllProductsQuery, IEnumerable<ProductReadModel>>
{
    private readonly IDbConnection _connection;

    public GetAllProductsQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<ProductReadModel>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetAllProductsQuery");

        var sqlQuery = @"
            SELECT
                Id,
                Name,
                Description,
                PriceAmount,
                PriceCurrency,
                Stock,
                LastUpdated
            FROM Products";

        var products = await _connection.QueryAsync<ProductReadModel>(sqlQuery);

        return products;
    }

    public async Task<IEnumerable<ProductReadModel>> HandleAsync(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}


