namespace StarterApp.Api.Application.Queries;

public class GetOrdersByCustomerQuery : IQuery<IEnumerable<OrderReadModel>>, IRequest<IEnumerable<OrderReadModel>>
{
    public int CustomerId { get; set; }
}

public class GetOrdersByCustomerQueryHandler : IRequestHandler<GetOrdersByCustomerQuery, IEnumerable<OrderReadModel>>
{
    private readonly string _connectionString;

    public GetOrdersByCustomerQueryHandler(IConfiguration configuration)
    {
        _connectionString = configuration.GetRequiredConnectionString();
    }

    public async Task<IEnumerable<OrderReadModel>> Handle(GetOrdersByCustomerQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByCustomerQuery for customer {CustomerId}", query.CustomerId);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst, 
                   TotalGstAmount, Currency, LastUpdated
            FROM Orders 
            WHERE CustomerId = @CustomerId
            ORDER BY OrderDate DESC";

        var orders = await connection.QueryAsync<OrderReadModel>(sql, new { CustomerId = query.CustomerId });

        return orders;
    }

    public async Task<IEnumerable<OrderReadModel>> HandleAsync(GetOrdersByCustomerQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}


