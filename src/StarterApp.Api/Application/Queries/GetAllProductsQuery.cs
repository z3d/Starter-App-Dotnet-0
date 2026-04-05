namespace StarterApp.Api.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductReadModel>>, IRequest<IEnumerable<ProductReadModel>>, ICacheable
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string CacheKey => $"Products:All:Page:{Page}:Size:{PageSize}";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(5);
}

public class GetAllProductsQueryHandler : IRequestHandler<GetAllProductsQuery, IEnumerable<ProductReadModel>>
{
    private readonly IDbConnection _connection;

    public GetAllProductsQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<ProductReadModel>> HandleAsync(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetAllProductsQuery (page {Page}, size {PageSize})", query.Page, query.PageSize);

        var offset = (query.Page - 1) * query.PageSize;

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
            ORDER BY Id
            OFFSET @Offset ROWS FETCH NEXT @FetchSize ROWS ONLY";

        return await _connection.QueryAsync<ProductReadModel>(
            new CommandDefinition(sqlQuery, new { Offset = offset, FetchSize = query.PageSize + 1 }, cancellationToken: cancellationToken));
    }
}
