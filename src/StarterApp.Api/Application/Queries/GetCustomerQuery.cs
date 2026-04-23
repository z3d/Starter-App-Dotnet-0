namespace StarterApp.Api.Application.Queries;

public class GetCustomerQuery : IQuery<CustomerReadModel?>, IRequest<CustomerReadModel?>, ICacheable
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

    public GetCustomerQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<CustomerReadModel?> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomerQuery for customer {Id}", query.Id);

        var sqlQuery = @"
            SELECT
                Id,
                Name,
                Email,
                DateCreated,
                IsActive
            FROM Customers
            WHERE Id = @Id";

        return await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QueryFirstOrDefaultAsync<CustomerReadModel>(
                new CommandDefinition(sqlQuery, new { Id = query.Id }, cancellationToken: ct)),
            cancellationToken);
    }
}


