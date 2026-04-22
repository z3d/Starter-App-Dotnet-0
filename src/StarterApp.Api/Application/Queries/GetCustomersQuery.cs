namespace StarterApp.Api.Application.Queries;

public class GetCustomersQuery : IQuery<IEnumerable<CustomerReadModel>>, IRequest<IEnumerable<CustomerReadModel>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, IEnumerable<CustomerReadModel>>
{
    private readonly IDbConnection _connection;

    public GetCustomersQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<CustomerReadModel>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomersQuery (page {Page}, size {PageSize})", query.Page, query.PageSize);

        var offset = (query.Page - 1) * query.PageSize;

        var sqlQuery = @"
            SELECT
                Id,
                Name,
                Email,
                DateCreated,
                IsActive
            FROM Customers
            ORDER BY Id
            OFFSET @Offset ROWS FETCH NEXT @FetchSize ROWS ONLY";

        return await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QueryAsync<CustomerReadModel>(
                new CommandDefinition(sqlQuery, new { Offset = offset, FetchSize = query.PageSize + 1 }, cancellationToken: ct)),
            cancellationToken);
    }
}
