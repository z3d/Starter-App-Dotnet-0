namespace StarterApp.Api.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductReadModel>>, IRequest<IEnumerable<ProductReadModel>>, IOwnerScopedRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GetAllProductsQueryHandler : IRequestHandler<GetAllProductsQuery, IEnumerable<ProductReadModel>>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetAllProductsQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<IEnumerable<ProductReadModel>> HandleAsync(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetAllProductsQuery (page {Page}, size {PageSize})", query.Page, query.PageSize);

        var offset = (query.Page - 1) * query.PageSize;
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

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
            WHERE OwnerSubject = @OwnerSubject
              AND TenantId = @TenantId
            ORDER BY Id
            OFFSET @Offset ROWS FETCH NEXT @FetchSize ROWS ONLY";

        return await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QueryAsync<ProductReadModel>(
                new CommandDefinition(sqlQuery,
                    new { ownerScope.OwnerSubject, ownerScope.TenantId, Offset = offset, FetchSize = query.PageSize + 1 },
                    cancellationToken: ct)),
            cancellationToken);
    }
}
