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
            SELECT o.id AS ""Id"",
                   o.customer_id AS ""CustomerId"",
                   o.order_date AS ""OrderDate"",
                   o.status AS ""Status"",
                   COALESCE(t.total_excluding_gst, 0) AS ""TotalExcludingGst"",
                   COALESCE(t.total_including_gst, 0) AS ""TotalIncludingGst"",
                   COALESCE(t.total_gst_amount, 0) AS ""TotalGstAmount"",
                   COALESCE(t.currency, 'USD') AS ""Currency"",
                   o.last_updated AS ""LastUpdated""
            FROM orders o
            LEFT JOIN LATERAL (
                SELECT SUM(unit_price_excluding_gst * quantity) AS total_excluding_gst,
                       SUM((unit_price_excluding_gst + round(unit_price_excluding_gst * gst_rate, 2)) * quantity) AS total_including_gst,
                       SUM(round(unit_price_excluding_gst * gst_rate, 2) * quantity) AS total_gst_amount,
                       MIN(currency) AS currency
                FROM order_items
                WHERE order_id = o.id
            ) t ON TRUE
            WHERE o.id = @Id
              AND o.owner_subject = @OwnerSubject
              AND o.tenant_id = @TenantId";

        const string itemsSql = @"
            SELECT order_id AS ""OrderId"",
                   product_id AS ""ProductId"",
                   product_name AS ""ProductName"",
                   quantity AS ""Quantity"",
                   unit_price_excluding_gst AS ""UnitPriceExcludingGst"",
                   unit_price_excluding_gst + round(unit_price_excluding_gst * gst_rate, 2) AS ""UnitPriceIncludingGst"",
                   unit_price_excluding_gst * quantity AS ""TotalPriceExcludingGst"",
                   (unit_price_excluding_gst + round(unit_price_excluding_gst * gst_rate, 2)) * quantity AS ""TotalPriceIncludingGst"",
                   gst_rate AS ""GstRate"",
                   currency AS ""Currency""
            FROM order_items
            WHERE order_id = @Id";

        var order = await PostgresRetryPolicy.ExecuteAsync(
            ct => _connection.QuerySingleOrDefaultAsync<OrderWithItemsReadModel>(
                new CommandDefinition(orderSql,
                    new { query.Id, ownerScope.OwnerSubject, ownerScope.TenantId },
                    cancellationToken: ct)),
            cancellationToken);

        if (order == null)
            return null;

        var items = await PostgresRetryPolicy.ExecuteAsync(
            ct => _connection.QueryAsync<OrderItemReadModel>(
                new CommandDefinition(itemsSql, new { Id = query.Id }, cancellationToken: ct)),
            cancellationToken);
        order.Items = items.ToList();

        return order;
    }
}
