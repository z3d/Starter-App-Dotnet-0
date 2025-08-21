namespace StarterApp.Api.Application.Commands;

public class UpdateOrderStatusCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpdateOrderStatusCommandHandler : ICommandHandler<UpdateOrderStatusCommand>,
                                             IRequestHandler<UpdateOrderStatusCommand, OrderDto>
{
    private readonly IOrderCommandService _commandService;

    public UpdateOrderStatusCommandHandler(IOrderCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateOrderStatusCommand for order {OrderId} to status {Status}",
            command.OrderId, command.Status);

        var status = Enum.Parse<OrderStatus>(command.Status);
        await _commandService.UpdateOrderStatusAsync(command.OrderId, status);
    }

    public async Task<OrderDto> HandleAsync(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateOrderStatusCommand to return OrderDto for order {OrderId}", command.OrderId);

        var status = Enum.Parse<OrderStatus>(command.Status);
        var updatedOrder = await _commandService.UpdateOrderStatusAsync(command.OrderId, status);

        if (updatedOrder == null)
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");

        return MapToOrderDto(updatedOrder);
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



