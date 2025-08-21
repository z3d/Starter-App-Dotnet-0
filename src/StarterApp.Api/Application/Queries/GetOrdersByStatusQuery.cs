using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;

namespace StarterApp.Api.Application.Queries;

public class GetOrdersByStatusQuery : IQuery<IEnumerable<OrderDto>>, IRequest<IEnumerable<OrderDto>>
{
    public string Status { get; set; } = string.Empty;
}

public class GetOrdersByStatusQueryHandler : IQueryHandler<GetOrdersByStatusQuery, IEnumerable<OrderDto>>, 
                                           IRequestHandler<GetOrdersByStatusQuery, IEnumerable<OrderDto>>
{
    private readonly string _connectionString;

    public GetOrdersByStatusQueryHandler(IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ?? 
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<IEnumerable<OrderDto>> Handle(GetOrdersByStatusQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByStatusQuery for status {Status}", query.Status);
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT Id, CustomerId, OrderDate, Status, TotalExcludingGst, TotalIncludingGst, 
                   TotalGstAmount, Currency, LastUpdated
            FROM Orders 
            WHERE Status = @Status
            ORDER BY OrderDate DESC";

        var orders = await connection.QueryAsync<OrderReadModel>(sql, new { Status = query.Status });
        
        return orders.Select(MapToDto);
    }

    private static OrderDto MapToDto(OrderReadModel readModel)
    {
        return new OrderDto
        {
            Id = readModel.Id,
            CustomerId = readModel.CustomerId,
            OrderDate = readModel.OrderDate,
            Status = readModel.Status,
            Items = [], // Items not loaded for this query for performance
            TotalExcludingGst = readModel.TotalExcludingGst,
            TotalIncludingGst = readModel.TotalIncludingGst,
            TotalGstAmount = readModel.TotalGstAmount,
            Currency = readModel.Currency,
            LastUpdated = readModel.LastUpdated
        };
    }
}