namespace StarterApp.Api.Application.Queries;

public class GetCustomerQuery : IQuery<CustomerReadModel?>, IRequest<CustomerReadModel?>, ICacheable, IOwnerScopedRequest
{
    public int Id { get; }
    public string CacheKey => $"Customer:{Id}";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);

    public GetCustomerQuery(int id)
    {
        Id = id;
    }
}

public class GetCustomerQueryHandler : IRequestHandler<GetCustomerQuery, CustomerReadModel?>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetCustomerQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<CustomerReadModel?> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomerQuery for customer {Id}", query.Id);
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        var sqlQuery = @"
            SELECT
                Id,
                Name,
                Email,
                DateCreated,
                IsActive
            FROM Customers
            WHERE Id = @Id
              AND OwnerSubject = @OwnerSubject
              AND TenantId = @TenantId";

        return await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QueryFirstOrDefaultAsync<CustomerReadModel>(
                new CommandDefinition(sqlQuery,
                    new { query.Id, ownerScope.OwnerSubject, ownerScope.TenantId },
                    cancellationToken: ct)),
            cancellationToken);
    }
}
