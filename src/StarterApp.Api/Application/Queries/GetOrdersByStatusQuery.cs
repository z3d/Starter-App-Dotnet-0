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
            SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst,
                   TotalGstAmount, Currency, LastUpdated
            FROM Orders
            WHERE Status = @Status
            ORDER BY OrderDate DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        return await _connection.QueryAsync<OrderReadModel>(sql,
            new { Status = query.Status, Offset = offset, PageSize = query.PageSize });
    }
}
