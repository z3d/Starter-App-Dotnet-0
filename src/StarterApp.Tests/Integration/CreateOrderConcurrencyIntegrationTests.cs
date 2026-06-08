using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StarterApp.Tests.Integration;

// Real-Postgres coverage for two CreateOrder correctness invariants that EF InMemory cannot model:
// (1) retry idempotency on a commit-unknown transient failover, and (2) no oversell under concurrent
// reservation of the last unit (the atomic UPDATE ... WHERE Stock >= qty).
[Collection("Integration Tests")]
public class CreateOrderConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public CreateOrderConcurrencyIntegrationTests(ApiTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateOrder_WhenTransientFaultAfterCommit_RetriesWithoutCreatingSecondOrderOrDoubleReservingStock()
    {
        var (customerId, productId) = await SeedCustomerAndProductAsync(stock: 5);

        // Context with retry enabled (mirrors production) + an interceptor that throws a transient
        // fault AFTER the first commit succeeds — the commit-unknown scenario. The execution strategy
        // then retries the whole delegate; the handler's stable orderId + committed-order re-check
        // must avoid a second order / second stock reservation.
        var interceptor = new ThrowTransientOnceAfterCommitInterceptor();
        await using var context = CreateRetryingContext(interceptor);
        var handler = new CreateOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
        var command = new CreateOrderCommand
        {
            CustomerId = customerId,
            Items = [new CreateOrderItemCommand { ProductId = productId, Quantity = 2 }]
        };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(interceptor.Fired, "The interceptor should have thrown once, forcing a retry.");

        await using var verify = CreateContext();
        var orders = await verify.Orders.ToListAsync();
        Assert.Single(orders);                       // exactly one order despite the retry
        Assert.Equal(result.Id, orders[0].Id);       // same stable id returned and persisted
        var product = await verify.Products.FindAsync(productId);
        Assert.Equal(3, product!.Stock);             // 5 - 2, decremented exactly once
    }

    [Fact]
    public async Task CreateOrder_TwoConcurrentOrdersForLastUnit_OnlyOneSucceeds()
    {
        var (customerId, productId) = await SeedCustomerAndProductAsync(stock: 1);

        async Task<bool> TryCreateAsync()
        {
            await using var context = CreateContext();
            var handler = new CreateOrderCommandHandler(context, NullCacheInvalidator.Instance, TestOwnerOnlyPolicy.Instance);
            var command = new CreateOrderCommand
            {
                CustomerId = customerId,
                Items = [new CreateOrderItemCommand { ProductId = productId, Quantity = 1 }]
            };
            try
            {
                await handler.HandleAsync(command, CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false; // "Insufficient stock" — the atomic UPDATE matched zero rows
            }
        }

        var results = await Task.WhenAll(TryCreateAsync(), TryCreateAsync());

        Assert.Equal(1, results.Count(succeeded => succeeded)); // exactly one wins the last unit
        await using var verify = CreateContext();
        Assert.Single(await verify.Orders.ToListAsync());
        var product = await verify.Products.FindAsync(productId);
        Assert.Equal(0, product!.Stock); // no oversell
    }

    private async Task<(int customerId, int productId)> SeedCustomerAndProductAsync(int stock)
    {
        await using var seed = CreateContext();
        var customer = TestEntities.Customer("Concurrency Customer", Email.Create("concurrency@example.com"));
        var product = TestEntities.Product("Concurrency Product", "Desc", Money.Create(10.00m, "USD"), stock);
        seed.Customers.Add(customer);
        seed.Products.Add(product);
        await seed.SaveChangesAsync();
        return (customer.Id, product.Id);
    }

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_fixture.ConnectionString).Options);

    private ApplicationDbContext CreateRetryingContext(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .AddInterceptors(interceptor)
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class ThrowTransientOnceAfterCommitInterceptor : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                // SqlState 40001 (serialization_failure) is transient → the retrying execution strategy
                // re-runs the delegate. The row is already committed, so this is a faithful
                // commit-succeeded-but-client-saw-an-error scenario.
                throw new Npgsql.PostgresException("simulated transient failover after commit", "ERROR", "ERROR", "40001");
            }

            return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        }
    }
}
