namespace StarterApp.Api.Application.Queries;

public class GetCustomersQuery : IQuery<IEnumerable<CustomerReadModel>>, IRequest<IEnumerable<CustomerReadModel>>
{
}

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, IEnumerable<CustomerReadModel>>
{
    private readonly IDbConnection _connection;

    public GetCustomersQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<CustomerReadModel>> Handle(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomersQuery");

        var sqlQuery = @"
            SELECT
                Id,
                Name,
                Email,
                DateCreated,
                IsActive
            FROM Customers";

        var customers = await _connection.QueryAsync<CustomerReadModel>(sqlQuery);

        return customers;
    }

    public async Task<IEnumerable<CustomerReadModel>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}


