namespace StarterApp.Api.Application.Commands;

public class CancelOrderCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
}

public class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand>, 
                                       IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly IOrderCommandService _commandService;

    public CancelOrderCommandHandler(IOrderCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CancelOrderCommand for order {OrderId}", command.OrderId);
        
        await _commandService.CancelOrderAsync(command.OrderId);
    }

    async Task<OrderDto> IRequestHandler<CancelOrderCommand, OrderDto>.Handle(
        CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CancelOrderCommand to return OrderDto for order {OrderId}", command.OrderId);
        
        var cancelledOrder = await _commandService.CancelOrderAsync(command.OrderId);

        if (cancelledOrder == null)
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");

        return MapToOrderDto(cancelledOrder);
    }

    private static OrderDto MapToOrderDto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Items = order.Items.Select(item => new OrderItemDto
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPriceExcludingGst = item.UnitPriceExcludingGst.Amount,
                UnitPriceIncludingGst = item.GetUnitPriceIncludingGst().Amount,
                TotalPriceExcludingGst = item.GetTotalPriceExcludingGst().Amount,
                TotalPriceIncludingGst = item.GetTotalPriceIncludingGst().Amount,
                GstRate = item.GstRate,
                Currency = item.UnitPriceExcludingGst.Currency
            }).ToList(),
            TotalExcludingGst = order.GetTotalExcludingGst().Amount,
            TotalIncludingGst = order.GetTotalIncludingGst().Amount,
            TotalGstAmount = order.GetTotalGstAmount().Amount,
            Currency = order.Items.FirstOrDefault()?.UnitPriceExcludingGst.Currency ?? "USD",
            LastUpdated = order.LastUpdated
        };
    }
}