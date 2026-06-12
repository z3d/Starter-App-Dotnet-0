namespace StarterApp.Api.Application.Queries;

public class GetCustomersQuery : IQuery<IEnumerable<CustomerReadModel>>, IOwnerScopedRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, IEnumerable<CustomerReadModel>>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetCustomersQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<IEnumerable<CustomerReadModel>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomersQuery (page {Page}, size {PageSize})", query.Page, query.PageSize);

        var offset = (query.Page - 1) * query.PageSize;
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        var sqlQuery = @"
            SELECT
                id AS ""Id"",
                name AS ""Name"",
                email AS ""Email"",
                date_created AS ""DateCreated"",
                is_active AS ""IsActive""
            FROM customers
            WHERE owner_subject = @OwnerSubject
              AND tenant_id = @TenantId
            ORDER BY id
            LIMIT @FetchSize OFFSET @Offset";

        return await PostgresRetryPolicy.ExecuteAsync(
            ct => _connection.QueryAsync<CustomerReadModel>(
                new CommandDefinition(sqlQuery,
                    new { ownerScope.OwnerSubject, ownerScope.TenantId, Offset = offset, FetchSize = query.PageSize + 1 },
                    cancellationToken: ct)),
            cancellationToken);
    }
}
