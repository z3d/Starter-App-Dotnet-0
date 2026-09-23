namespace StarterApp.Tests.Infrastructure;

// The domain reads no clock. This is the one place a timestamp enters a saved row, so it pins
// the contract: one instant per SaveChanges lands on the audit columns and on every event.
public class DomainEventsInterceptorTests
{
    private static readonly DateTimeOffset First = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = First.AddHours(6);

    private static ApplicationDbContext CreateDbContext(string databaseName, TimeProvider clock)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new DomainEventsInterceptor(clock))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SaveChanges_StampsAuditColumnsAndEventTimeFromOneClockRead()
    {
        var dbName = Guid.NewGuid().ToString();
        var clock = new SettableTimeProvider(First);

        var product = TestEntities.Product();
        var order = TestEntities.Order(customerId: 42);
        order.AddItem(7, product.Name, 1, product.Price);

        await using (var context = CreateDbContext(dbName, clock))
        {
            context.Products.Add(product);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        Assert.Equal(First, product.DateCreated);
        Assert.Equal(First, product.LastUpdated);
        Assert.Equal(First, order.DateCreated);
        Assert.Equal(First, order.LastUpdated);

        await using (var context = CreateDbContext(dbName, clock))
        {
            var created = Assert.Single(await context.OutboxMessages.ToListAsync());
            Assert.Equal(OrderCreatedDomainEvent.Contract, created.Type);
            Assert.Equal(First, created.OccurredOnUtc);
        }

        clock.UtcNow = Second;

        await using (var context = CreateDbContext(dbName, clock))
        {
            var tracked = await context.Orders.Include(o => o.Items).SingleAsync();
            tracked.UpdateStatus(OrderStatus.Confirmed);
            await context.SaveChangesAsync();

            Assert.Equal(First, tracked.DateCreated);
            Assert.Equal(Second, tracked.LastUpdated);

            var statusChanged = Assert.Single(
                await context.OutboxMessages.Where(m => m.Type == OrderStatusChangedDomainEvent.Contract).ToListAsync());
            Assert.Equal(Second, statusChanged.OccurredOnUtc);
            Assert.Contains($"\"LastUpdated\":\"{Second:yyyy-MM-dd'T'HH:mm:ss}", statusChanged.Payload);
        }
    }

    private sealed class SettableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
