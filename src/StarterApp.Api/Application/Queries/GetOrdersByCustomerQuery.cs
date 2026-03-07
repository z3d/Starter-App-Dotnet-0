namespace StarterApp.Api.Application.Queries;

public class GetOrdersByCustomerQuery : IQuery<IEnumerable<OrderReadModel>>, IRequest<IEnumerable<OrderReadModel>>
{
    public int CustomerId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GetOrdersByCustomerQueryHandler : IRequestHandler<GetOrdersByCustomerQuery, IEnumerable<OrderReadModel>>
{
    private readonly IDbConnection _connection;

    public GetOrdersByCustomerQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<OrderReadModel>> HandleAsync(GetOrdersByCustomerQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByCustomerQuery for customer {CustomerId} (page {Page}, size {PageSize})",
            query.CustomerId, query.Page, query.PageSize);

        var offset = (query.Page - 1) * query.PageSize;

        const string sql = @"
            SELECT o.Id, o.CustomerId, o.OrderDate, o.Status,
                   ISNULL(t.TotalExcludingGst, 0) AS TotalExcludingGst,
                   ISNULL(t.TotalIncludingGst, 0) AS TotalIncludingGst,
                   ISNULL(t.TotalGstAmount, 0) AS TotalGstAmount,
                   ISNULL(t.Currency, o.Currency) AS Currency,
                   o.LastUpdated
            FROM Orders o
            OUTER APPLY (
                SELECT SUM(UnitPriceExcludingGst * Quantity) AS TotalExcludingGst,
                       SUM(UnitPriceExcludingGst * Quantity * (1 + GstRate)) AS TotalIncludingGst,
                       SUM(UnitPriceExcludingGst * Quantity * GstRate) AS TotalGstAmount,
                       MIN(Currency) AS Currency
                FROM OrderItems
                WHERE OrderId = o.Id
            ) t
            WHERE o.CustomerId = @CustomerId
            ORDER BY o.OrderDate DESC
            OFFSET @Offset ROWS FETCH NEXT @FetchSize ROWS ONLY";

        return await _connection.QueryAsync<OrderReadModel>(sql,
            new { CustomerId = query.CustomerId, Offset = offset, FetchSize = query.PageSize + 1 });
    }
}
