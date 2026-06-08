using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Integration;

// The outbox claim query uses PostgreSQL's FOR UPDATE SKIP LOCKED so multiple OutboxProcessor
// replicas can claim disjoint batches without blocking or double-publishing. Every other outbox
// test runs on EF InMemory, which ignores row locking entirely — so this is the only place that
// concurrency-safety property is actually exercised against real PostgreSQL.
[Collection("Integration Tests")]
public class OutboxClaimConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public OutboxClaimConcurrencyIntegrationTests(ApiTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Proves SKIP LOCKED specifically (not just "no double-claim"): a plain FOR UPDATE would also
    // avoid double-claims, but by BLOCKING. Here one transaction holds row locks open while a second
    // reader issues the same claim SELECT — SKIP LOCKED must return zero rows immediately rather than
    // wait. The timeout converts a (broken) blocking implementation into a clear test failure.
    [Fact]
    public async Task ClaimSelect_WhileAnotherTransactionHoldsRowLocks_SkipsThemInsteadOfBlocking()
    {
        await SeedOutboxMessagesAsync(count: 6);
        var processor = CreateClaimOnlyProcessor(batchSize: 100);
        var now = DateTimeOffset.UtcNow;

        await using var contextA = CreateContext();
        await using var contextB = CreateContext();

        // A locks every unprocessed row (FOR UPDATE) and keeps the transaction open — no commit.
        await using var transactionA = await contextA.Database.BeginTransactionAsync();
        var lockedByA = await processor.ClaimUnprocessedMessagesAsync(contextA, now, CancellationToken.None);
        Assert.Equal(6, lockedByA.Count);

        // B issues the same claim SELECT on a separate connection while A still holds the locks.
        using var bTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seenByB = await processor.ClaimUnprocessedMessagesAsync(contextB, now, bTimeout.Token);

        // SKIP LOCKED: B skips A's locked rows and returns nothing, without blocking on the timeout.
        Assert.Empty(seenByB);

        await transactionA.RollbackAsync();
    }

    // The end-to-end safety property: two processors claiming concurrently must partition the work —
    // no message claimed by both, and every row claimed exactly once.
    [Fact]
    public async Task ConcurrentClaimers_NeverClaimTheSameMessage_AndCoverAllRows()
    {
        const int total = 20;
        const int batchSize = 10;
        await SeedOutboxMessagesAsync(total);

        var processorA = CreateClaimOnlyProcessor(batchSize);
        var processorB = CreateClaimOnlyProcessor(batchSize);
        var now = DateTimeOffset.UtcNow;
        var lockedUntil = now.AddMinutes(5);
        var processingIdA = Guid.CreateVersion7();
        var processingIdB = Guid.CreateVersion7();

        await using var contextA = CreateContext();
        await using var contextB = CreateContext();

        // Real concurrency: independent connections/transactions claiming at the same time.
        var results = await Task.WhenAll(
            processorA.ClaimBatchInTransactionAsync(contextA, processingIdA, now, lockedUntil, CancellationToken.None),
            processorB.ClaimBatchInTransactionAsync(contextB, processingIdB, now, lockedUntil, CancellationToken.None));

        var idsA = results[0].Select(message => message.Id).ToHashSet();
        var idsB = results[1].Select(message => message.Id).ToHashSet();

        // Core invariant: no message claimed by both processors.
        Assert.Empty(idsA.Intersect(idsB));
        Assert.True(idsA.Count <= batchSize, $"Processor A claimed {idsA.Count}, exceeding its batch size {batchSize}.");
        Assert.True(idsB.Count <= batchSize, $"Processor B claimed {idsB.Count}, exceeding its batch size {batchSize}.");
        // Together they covered every seeded row exactly once.
        Assert.Equal(total, idsA.Count + idsB.Count);

        // Persisted state: every row owned by exactly one of the two processing ids, none left unclaimed.
        await using var verifyContext = CreateContext();
        var rows = await verifyContext.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.Equal(total, rows.Count);
        Assert.All(rows, row => Assert.Contains(row.ProcessingId, new Guid?[] { processingIdA, processingIdB }));
    }

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_fixture.ConnectionString).Options);

    private async Task SeedOutboxMessagesAsync(int count)
    {
        await using var context = CreateContext();
        for (var i = 0; i < count; i++)
            context.OutboxMessages.Add(OutboxMessage.Create(new ClaimTestEvent(DateTimeOffset.UtcNow.AddSeconds(i))));
        await context.SaveChangesAsync();
    }

    // The claim methods touch only the DbContext (passed per call) and BatchSize; the scope factory,
    // Service Bus sender, and capture sink are never used on the claim path, so mocks suffice.
    private static OutboxProcessor CreateClaimOnlyProcessor(int batchSize)
    {
        var options = Options.Create(new OutboxProcessorOptions { BatchSize = batchSize, LockDurationSeconds = 300 });
        return new OutboxProcessor(
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<ServiceBusSender>(),
            Mock.Of<IPayloadCaptureSink>(),
            options,
            new LoggerFactory().CreateLogger<OutboxProcessor>());
    }

    private sealed record ClaimTestEvent(DateTimeOffset OccurredOnUtc) : IDomainEvent
    {
        public string EventType => "outbox.claim.test.v1";
    }
}
