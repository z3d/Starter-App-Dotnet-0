using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;

namespace StarterApp.Api.Application.Queries;

public class GetOrderByIdQuery : IQuery<OrderDto?>, IRequest<OrderDto?>
{
    public int Id { get; set; }
}

public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto?>, 
                                       IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly IOrderQueryService _queryService;

    public GetOrderByIdQueryHandler(IOrderQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<OrderDto?> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetOrderByIdQuery for order {Id}", query.Id);
        
        var orderWithItems = await _queryService.GetOrderByIdAsync(query.Id);
        
        return orderWithItems == null ? null : MapToDto(orderWithItems);
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