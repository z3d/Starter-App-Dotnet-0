namespace StarterApp.Api.Application.Queries;

public class GetProductByIdQuery : IQuery<ProductReadModel?>, IRequest<ProductReadModel?>, ICacheable, IOwnerScopedRequest
{
    public int Id { get; }
    public string CacheKey => $"Product:{Id}";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);
    public TimeSpan CacheRefreshWindow => TimeSpan.FromMinutes(1);

    public GetProductByIdQuery(int id)
    {
        Id = id;
    }
}

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductReadModel?>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetProductByIdQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<ProductReadModel?> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetProductByIdQuery for product {Id}", query.Id);
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        var sqlQuery = @"
            SELECT
                id AS ""Id"",
                name AS ""Name"",
                description AS ""Description"",
                price_amount AS ""PriceAmount"",
                price_currency AS ""PriceCurrency"",
                stock AS ""Stock"",
                last_updated AS ""LastUpdated""
            FROM products
            WHERE id = @Id
              AND owner_subject = @OwnerSubject
              AND tenant_id = @TenantId";

        return await PostgresRetryPolicy.ExecuteAsync(
            ct => _connection.QueryFirstOrDefaultAsync<ProductReadModel>(
                new CommandDefinition(sqlQuery,
                    new { query.Id, ownerScope.OwnerSubject, ownerScope.TenantId },
                    cancellationToken: ct)),
            cancellationToken);
    }
}
