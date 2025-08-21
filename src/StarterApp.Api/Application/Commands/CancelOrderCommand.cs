using StarterApp.Api.Data;
using Serilog;

namespace StarterApp.Api.Application.Commands;

public class CancelOrderCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;

    public CancelOrderCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CancelOrderCommand for order {OrderId}", command.OrderId);

        await CancelOrderAsync(command.OrderId);
    }

    public async Task<OrderDto> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CancelOrderCommand to return OrderDto for order {OrderId}", command.OrderId);

        var cancelledOrder = await CancelOrderAsync(command.OrderId);

        if (cancelledOrder == null)
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");

        return MapToOrderDto(cancelledOrder);
    }

    private async Task<Order?> CancelOrderAsync(int orderId)
    {
        Log.Information("Cancelling order {OrderId} with EF Core", orderId);

        var order = await LoadOrderWithItems(orderId);
        if (order == null)
        {
            Log.Warning("Order {OrderId} not found for cancellation", orderId);
            return null;
        }

        order.Cancel();
        _dbContext.Orders.Update(order);

        await _dbContext.SaveChangesAsync();
        return order;
    }

    private async Task<Order?> LoadOrderWithItems(int orderId)
    {
        var order = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return null;

        var orderItems = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(oi => oi.OrderId == orderId)
            .ToListAsync();

        // Reconstruct the order with items
        var orderWithItems = new Order(order.CustomerId);
        orderWithItems.SetId(order.Id);
        orderWithItems.LoadFromDatabase(order.OrderDate, order.Status, order.LastUpdated, orderItems);

        return orderWithItems;
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



