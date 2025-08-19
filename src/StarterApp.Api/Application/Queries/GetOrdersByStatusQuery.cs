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
    private readonly IOrderQueryService _queryService;

    public GetOrdersByStatusQueryHandler(IOrderQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<IEnumerable<OrderDto>> Handle(GetOrdersByStatusQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByStatusQuery for status {Status}", query.Status);
        
        var orders = await _queryService.GetOrdersByStatusAsync(query.Status);
        
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