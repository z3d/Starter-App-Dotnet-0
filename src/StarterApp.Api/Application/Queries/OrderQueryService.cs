using StarterApp.Api.Application.ReadModels;
using Dapper;
using System.Data;

namespace StarterApp.Api.Application.Queries;

/// <summary>
/// Service for order read operations using Dapper for optimized queries
/// </summary>
public class OrderQueryService : IOrderQueryService
{
    private readonly IDbConnection _dbConnection;

    public OrderQueryService(IDbConnection dbConnection)
    {
        _dbConnection = dbConnection;
    }

    public async Task<OrderWithItemsReadModel?> GetOrderByIdAsync(int id)
    {
        Log.Information("Getting order {Id} with items using Dapper", id);

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

        var order = await _dbConnection.QuerySingleOrDefaultAsync<OrderWithItemsReadModel>(orderSql, new { Id = id });
        
        if (order == null)
            return null;

        var items = await _dbConnection.QueryAsync<OrderItemReadModel>(itemsSql, new { Id = id });
        order.Items = items.ToList();

        return order;
    }

    public async Task<IEnumerable<OrderReadModel>> GetOrdersByCustomerAsync(int customerId)
    {
        Log.Information("Getting orders for customer {CustomerId} using Dapper", customerId);

        const string sql = @"
            SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst, 
                   TotalGstAmount, Currency, LastUpdated
            FROM Orders 
            WHERE CustomerId = @CustomerId
            ORDER BY OrderDate DESC";

        return await _dbConnection.QueryAsync<OrderReadModel>(sql, new { CustomerId = customerId });
    }

    public async Task<IEnumerable<OrderReadModel>> GetOrdersByStatusAsync(string status)
    {
        Log.Information("Getting orders with status {Status} using Dapper", status);

        const string sql = @"
            SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst, 
                   TotalGstAmount, Currency, LastUpdated
            FROM Orders 
            WHERE Status = @Status
            ORDER BY OrderDate DESC";

        return await _dbConnection.QueryAsync<OrderReadModel>(sql, new { Status = status });
    }
}