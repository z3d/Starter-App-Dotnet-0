namespace StarterApp.Api.Application.Commands;

internal static class OrderCancellationService
{
    public static async Task CancelAndRestoreStockAsync(ApplicationDbContext dbContext, Order order, CancellationToken cancellationToken)
    {
        order.Cancel();

        foreach (var item in order.Items)
        {
            var product = await dbContext.Products.FindAsync([item.ProductId], cancellationToken);
            if (product == null)
            {
                Log.Warning("Product {ProductId} no longer exists; cannot restore {Quantity} units of stock for order {OrderId}",
                    item.ProductId, item.Quantity, order.Id);
                continue;
            }

            product.UpdateStock(item.Quantity);
        }
    }
}
