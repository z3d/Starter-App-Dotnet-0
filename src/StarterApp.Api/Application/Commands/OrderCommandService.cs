using StarterApp.Api.Data;
using System.Reflection;

namespace StarterApp.Api.Application.Commands;
public class OrderCommandService : IOrderCommandService
{
    private readonly ApplicationDbContext _dbContext;

    public OrderCommandService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order> CreateOrderAsync(int customerId, List<CreateOrderItemCommand> items)
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

    public async Task<Order?> UpdateOrderStatusAsync(int orderId, OrderStatus status)
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

    public async Task<Order?> CancelOrderAsync(int orderId)
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
}
