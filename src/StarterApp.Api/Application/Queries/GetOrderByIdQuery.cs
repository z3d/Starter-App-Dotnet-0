using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;

namespace StarterApp.Api.Application.Queries;

public class GetOrderByIdQuery : IQuery<OrderWithItemsReadModel?>, IRequest<OrderWithItemsReadModel?>
{
    public int Id { get; set; }
}

public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderWithItemsReadModel?>,
                                       IRequestHandler<GetOrderByIdQuery, OrderWithItemsReadModel?>
{
    private readonly string _connectionString;

    public GetOrderByIdQueryHandler(IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ??
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<OrderWithItemsReadModel?> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrderByIdQuery for order {Id}", query.Id);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        Log.Information("Getting order {Id} with items using Dapper", query.Id);

        const string orderSql = @"
            SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst, 
                   TotalGstAmount, Currency, LastUpdated
            FROM Orders 
            WHERE Id = @Id";

        const string itemsSql = @"
            SELECT OrderId, ProductId, ProductName, Quantity, UnitPriceExcludingGst, 
                   UnitPriceExcludingGst * (1 + GstRate) AS UnitPriceIncludingGst,
                   UnitPriceExcludingGst * Quantity AS TotalPriceExcludingGst,
                   UnitPriceExcludingGst * Quantity * (1 + GstRate) AS TotalPriceIncludingGst,
                   GstRate, Currency
            FROM OrderItems 
            WHERE OrderId = @Id";

        var order = await connection.QuerySingleOrDefaultAsync<OrderWithItemsReadModel>(orderSql, new { Id = query.Id });

        if (order == null)
            return null;

        var items = await connection.QueryAsync<OrderItemReadModel>(itemsSql, new { Id = query.Id });
        order.Items = items.ToList();

        return order;
    }

    public async Task<OrderWithItemsReadModel?> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}



