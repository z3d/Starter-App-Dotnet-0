namespace StarterApp.Api.Application.Queries;

public class GetOrdersByStatusQuery : IQuery<IEnumerable<OrderReadModel>>, IRequest<IEnumerable<OrderReadModel>>
{
    public string Status { get; set; } = string.Empty;
}

public class GetOrdersByStatusQueryHandler : IRequestHandler<GetOrdersByStatusQuery, IEnumerable<OrderReadModel>>
{
    private readonly string _connectionString;

    public GetOrdersByStatusQueryHandler(IConfiguration configuration)
    {
        _connectionString = configuration.GetRequiredConnectionString();
    }

    public async Task<IEnumerable<OrderReadModel>> Handle(GetOrdersByStatusQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByStatusQuery for status {Status}", query.Status);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst, 
                   TotalGstAmount, Currency, LastUpdated
            FROM Orders 
            WHERE Status = @Status
            ORDER BY OrderDate DESC";

        var orders = await connection.QueryAsync<OrderReadModel>(sql, new { Status = query.Status });

        return orders;
    }

    public async Task<IEnumerable<OrderReadModel>> HandleAsync(GetOrdersByStatusQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}


