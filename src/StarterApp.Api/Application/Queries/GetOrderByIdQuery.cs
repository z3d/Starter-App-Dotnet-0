namespace StarterApp.Api.Application.Queries;

public class GetOrderByIdQuery : IQuery<OrderWithItemsReadModel?>, IRequest<OrderWithItemsReadModel?>, IOwnerScopedRequest
{
    public Guid Id { get; set; }
}

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderWithItemsReadModel?>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetOrderByIdQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<OrderWithItemsReadModel?> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrderByIdQuery for order {Id}", query.Id);
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        const string orderSql = @"
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
            WHERE o.Id = @Id
              AND o.OwnerSubject = @OwnerSubject
              AND o.TenantId = @TenantId";

        const string itemsSql = @"
            SELECT OrderId, ProductId, ProductName, Quantity, UnitPriceExcludingGst,
                   UnitPriceExcludingGst * (1 + GstRate) AS UnitPriceIncludingGst,
                   UnitPriceExcludingGst * Quantity AS TotalPriceExcludingGst,
                   UnitPriceExcludingGst * Quantity * (1 + GstRate) AS TotalPriceIncludingGst,
                   GstRate, Currency
            FROM OrderItems
            WHERE OrderId = @Id";

        var order = await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QuerySingleOrDefaultAsync<OrderWithItemsReadModel>(
                new CommandDefinition(orderSql,
                    new { query.Id, ownerScope.OwnerSubject, ownerScope.TenantId },
                    cancellationToken: ct)),
            cancellationToken);

        if (order == null)
            return null;

        var items = await SqlRetryPolicy.ExecuteAsync(
            ct => _connection.QueryAsync<OrderItemReadModel>(
                new CommandDefinition(itemsSql, new { Id = query.Id }, cancellationToken: ct)),
            cancellationToken);
        order.Items = items.ToList();

        return order;
    }
}
