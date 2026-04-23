namespace StarterApp.Api.Application.Queries;

public class GetOrdersByStatusQuery : IQuery<IEnumerable<OrderReadModel>>, IRequest<IEnumerable<OrderReadModel>>
{
    public string Status { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GetOrdersByStatusQueryHandler : IRequestHandler<GetOrdersByStatusQuery, IEnumerable<OrderReadModel>>
{
    private readonly IDbConnection _connection;

    public GetOrdersByStatusQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<OrderReadModel>> HandleAsync(GetOrdersByStatusQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByStatusQuery for status {Status} (page {Page}, size {PageSize})",
            query.Status, query.Page, query.PageSize);

        var offset = (query.Page - 1) * query.PageSize;

        const string sql = @"
            SELECT o.Id, o.CustomerId, o.OrderDate, o.Status,
                   ISNULL(t.TotalExcludingGst, 0) AS TotalExcludingGst,
                   ISNULL(t.TotalIncludingGst, 0) AS TotalIncludingGst,
                   ISNULL(t.TotalGstAmount, 0) AS TotalGstAmount,
                   ISNULL(t.Currency, 'USD') AS Currency,
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
            WHERE o.Status = @Status
            ORDER BY o.OrderDate DESC
            OFFSET @Offset ROWS FETCH NEXT @FetchSize ROWS ONLY";

        return await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QueryAsync<OrderReadModel>(
                new CommandDefinition(sql, new { Status = query.Status, Offset = offset, FetchSize = query.PageSize + 1 }, cancellationToken: ct)),
            cancellationToken);
    }
}
