using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;

namespace StarterApp.Api.Application.Queries;

public class GetOrdersByCustomerQuery : IQuery<IEnumerable<OrderDto>>, IRequest<IEnumerable<OrderDto>>
{
    public int CustomerId { get; set; }
}

public class GetOrdersByCustomerQueryHandler : IQueryHandler<GetOrdersByCustomerQuery, IEnumerable<OrderDto>>, 
                                             IRequestHandler<GetOrdersByCustomerQuery, IEnumerable<OrderDto>>
{
    private readonly IOrderQueryService _queryService;

    public GetOrdersByCustomerQueryHandler(IOrderQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<IEnumerable<OrderDto>> Handle(GetOrdersByCustomerQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrdersByCustomerQuery for customer {CustomerId}", query.CustomerId);
        
        var orders = await _queryService.GetOrdersByCustomerAsync(query.CustomerId);
        
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