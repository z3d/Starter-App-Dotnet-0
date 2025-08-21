using StarterApp.Api.Data;
using Serilog;

namespace StarterApp.Api.Application.Commands;

public class UpdateOrderStatusCommand : ICommand, IRequest<OrderDto>
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;

    public UpdateOrderStatusCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Handle(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateOrderStatusCommand for order {OrderId} to status {Status}",
            command.OrderId, command.Status);

        var status = Enum.Parse<OrderStatus>(command.Status);
        await UpdateOrderStatusAsync(command.OrderId, status);
    }

    public async Task<OrderDto> HandleAsync(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling UpdateOrderStatusCommand to return OrderDto for order {OrderId}", command.OrderId);

        var status = Enum.Parse<OrderStatus>(command.Status);
        var updatedOrder = await UpdateOrderStatusAsync(command.OrderId, status);

        if (updatedOrder == null)
            throw new KeyNotFoundException($"Order with ID {command.OrderId} was not found");

        return MapToOrderDto(updatedOrder);
    }

    private async Task<Order?> UpdateOrderStatusAsync(int orderId, OrderStatus status)
    {
        Log.Information("Updating order {OrderId} status to {Status} with EF Core", orderId, status);

        var order = await LoadOrderWithItems(orderId);
        if (order == null)
        {
            Log.Warning("Order {OrderId} not found for status update", orderId);
            return null;
        }

        order.UpdateStatus(status);
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



