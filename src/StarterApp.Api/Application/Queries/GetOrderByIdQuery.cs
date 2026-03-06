namespace StarterApp.Api.Application.Queries;

public class GetOrderByIdQuery : IQuery<OrderWithItemsReadModel?>, IRequest<OrderWithItemsReadModel?>
{
    public int Id { get; set; }
}

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderWithItemsReadModel?>
{
    private readonly IDbConnection _connection;

    public GetOrderByIdQueryHandler(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<OrderWithItemsReadModel?> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrderByIdQuery for order {Id}", query.Id);

        const string orderSql = @"
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
            WHERE o.Id = @Id";

        const string itemsSql = @"
            SELECT OrderId, ProductId, ProductName, Quantity, UnitPriceExcludingGst,
                   UnitPriceExcludingGst * (1 + GstRate) AS UnitPriceIncludingGst,
                   UnitPriceExcludingGst * Quantity AS TotalPriceExcludingGst,
                   UnitPriceExcludingGst * Quantity * (1 + GstRate) AS TotalPriceIncludingGst,
                   GstRate, Currency
            FROM OrderItems
            WHERE OrderId = @Id";

        var order = await _connection.QuerySingleOrDefaultAsync<OrderWithItemsReadModel>(orderSql, new { Id = query.Id });

        if (order == null)
            return null;

        var items = await _connection.QueryAsync<OrderItemReadModel>(itemsSql, new { Id = query.Id });
        order.Items = items.ToList();

        return order;
    }
}


