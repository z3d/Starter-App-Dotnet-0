using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;

namespace StarterApp.Api.Application.Queries;

public class GetOrderByIdQuery : IQuery<OrderDto?>, IRequest<OrderDto?>
{
    public int Id { get; set; }
}

public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto?>, 
                                       IRequestHandler<GetOrderByIdQuery, OrderDto?>
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

    public async Task<OrderDto?> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
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
        
        return MapToDto(order);
    }

    private static OrderDto MapToDto(OrderWithItemsReadModel readModel)
    {
        return new OrderDto
        {
            Id = readModel.Id,
            CustomerId = readModel.CustomerId,
            OrderDate = readModel.OrderDate,
            Status = readModel.Status,
            Items = readModel.Items.Select(item => new OrderItemDto
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPriceExcludingGst = item.UnitPriceExcludingGst,
                UnitPriceIncludingGst = item.UnitPriceIncludingGst,
                TotalPriceExcludingGst = item.TotalPriceExcludingGst,
                TotalPriceIncludingGst = item.TotalPriceIncludingGst,
                GstRate = item.GstRate,
                Currency = item.Currency
            }).ToList(),
            TotalExcludingGst = readModel.TotalExcludingGst,
            TotalIncludingGst = readModel.TotalIncludingGst,
            TotalGstAmount = readModel.TotalGstAmount,
            Currency = readModel.Currency,
            LastUpdated = readModel.LastUpdated
        };
    }
}