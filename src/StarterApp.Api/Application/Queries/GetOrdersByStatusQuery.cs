namespace StarterApp.Api.Application.Queries;

public class GetOrdersByStatusQuery : IQuery<IEnumerable<OrderReadModel>>, IRequest<IEnumerable<OrderReadModel>>, IOwnerScopedRequest
{
    public OrderStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GetOrdersByStatusQueryHandler : IRequestHandler<GetOrdersByStatusQuery, IEnumerable<OrderReadModel>>
{
    private readonly IDbConnection _connection;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetOrdersByStatusQueryHandler(IDbConnection connection, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _connection = connection;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<IEnumerable<OrderReadModel>> HandleAsync(GetOrdersByStatusQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByStatusQuery for status {Status} (page {Page}, size {PageSize})",
            query.Status, query.Page, query.PageSize);

        var offset = (query.Page - 1) * query.PageSize;
        var ownerScope = _ownerOnlyPolicy.GetRequiredScope();

        const string sql = @"
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
            WHERE o.status = @Status
              AND o.owner_subject = @OwnerSubject
              AND o.tenant_id = @TenantId
            ORDER BY o.order_date DESC, o.id DESC
            LIMIT @FetchSize OFFSET @Offset";

        return await PostgresRetryPolicy.ExecuteAsync(
            ct => _connection.QueryAsync<OrderReadModel>(
                new CommandDefinition(sql,
                    new { Status = query.Status!.Value.ToString(), ownerScope.OwnerSubject, ownerScope.TenantId, Offset = offset, FetchSize = query.PageSize + 1 },
                    cancellationToken: ct)),
            cancellationToken);
    }
}
