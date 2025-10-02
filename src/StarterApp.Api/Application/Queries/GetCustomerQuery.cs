namespace StarterApp.Api.Application.Queries;

public class GetCustomerQuery : IQuery<CustomerReadModel?>, IRequest<CustomerReadModel?>
{
    public int Id { get; }

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

    public async Task<CustomerReadModel?> Handle(GetCustomerQuery query, CancellationToken cancellationToken)
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

        var customer = await _connection.QueryFirstOrDefaultAsync<CustomerReadModel>(sqlQuery, new { Id = query.Id });

        if (customer == null)
        {
            Log.Warning("Customer with ID {Id} not found", query.Id);
            return null;
        }

        return customer;
    }

    public async Task<CustomerReadModel?> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}


