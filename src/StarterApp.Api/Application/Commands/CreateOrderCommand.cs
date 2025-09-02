using StarterApp.Api.Data;

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

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly ApplicationDbContext _dbContext;

    public CreateOrderCommandHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateOrderCommand for customer {CustomerId}", command.CustomerId);

        await CreateOrderAsync(command.CustomerId, command.Items);
    }

    public async Task<OrderDto> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        Log.Information("Handling CreateOrderCommand to return OrderDto for customer {CustomerId}", command.CustomerId);

        var createdOrder = await CreateOrderAsync(command.CustomerId, command.Items);

        return MapToOrderDto(createdOrder);
    }

    private async Task<Order> CreateOrderAsync(int customerId, List<CreateOrderItemCommand> items)
    {
        Log.Information("Creating order for customer {CustomerId} with EF Core", customerId);

        // Validate that customer exists
        var customerExists = await _dbContext.Customers.AnyAsync(c => c.Id == customerId);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer with ID {customerId} was not found");

        var order = new Order(customerId);

        // Create order header first
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Create order items as separate entities
        foreach (var itemCommand in items)
        {
            // Validate that product exists
            var product = await _dbContext.Products.FindAsync(itemCommand.ProductId);
            if (product == null)
                throw new KeyNotFoundException($"Product with ID {itemCommand.ProductId} was not found");

            var orderItem = new OrderItem(
                order.Id,
                itemCommand.ProductId,
                product.Name,
                itemCommand.Quantity,
                Money.Create(itemCommand.UnitPriceExcludingGst, itemCommand.Currency),
                itemCommand.GstRate
            );

            _dbContext.OrderItems.Add(orderItem);
        }

        await _dbContext.SaveChangesAsync();

        // Load the order with items for return
        return await LoadOrderWithItems(order.Id) ?? order;
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



