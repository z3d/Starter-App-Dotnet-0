namespace StarterApp.Api.Application.Commands;

public class CreateOrderCommand : ICommand, IRequest<OrderDto>
{
    public int CustomerId { get; set; }
    public List<CreateOrderItemCommand> Items { get; set; } = [];
}

public class CreateOrderItemCommand
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceExcludingGst { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal GstRate { get; set; } = OrderItem.DefaultGstRate;
}

public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>, 
                                       IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly IOrderCommandService _commandService;

    public CreateOrderCommandHandler(IOrderCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateOrderCommand for customer {CustomerId}", command.CustomerId);
        
        await _commandService.CreateOrderAsync(command.CustomerId, command.Items);
    }

    async Task<OrderDto> IRequestHandler<CreateOrderCommand, OrderDto>.Handle(
        CreateOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateOrderCommand to return OrderDto for customer {CustomerId}", command.CustomerId);
        
        var createdOrder = await _commandService.CreateOrderAsync(command.CustomerId, command.Items);

        return MapToOrderDto(createdOrder);
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