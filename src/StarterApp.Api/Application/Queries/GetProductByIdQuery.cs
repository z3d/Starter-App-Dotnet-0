namespace StarterApp.Api.Application.Queries;

public class GetProductByIdQuery : IQuery<ProductReadModel?>, IRequest<ProductReadModel?>, ICacheable, IOwnerScopedRequest
{
    public int Id { get; }
    public string CacheKey => $"Product:{Id}";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);

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
                Id,
                Name,
                Description,
                PriceAmount,
                PriceCurrency,
                Stock,
                LastUpdated
            FROM Products
            WHERE Id = @Id
              AND OwnerSubject = @OwnerSubject
              AND TenantId = @TenantId";

        return await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QueryFirstOrDefaultAsync<ProductReadModel>(
                new CommandDefinition(sqlQuery,
                    new { query.Id, ownerScope.OwnerSubject, ownerScope.TenantId },
                    cancellationToken: ct)),
            cancellationToken);
    }
}
