namespace StarterApp.Api.Data;

public interface IOrderRepository
{
    Task<Order?> LoadWithItemsAsync(int orderId);
}

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _dbContext;

    public OrderRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order?> LoadWithItemsAsync(int orderId)
    {
        var order = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return null;

        var orderItems = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(oi => oi.OrderId == orderId)
            .ToListAsync();

        return Order.Reconstitute(order.Id, order.CustomerId, order.OrderDate, order.Status, order.LastUpdated, orderItems);
    }
}
