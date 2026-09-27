using StarterApp.Functions;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class MessageInboxTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private NpgsqlDataSource _dataSource = null!;

    public MessageInboxTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
    }

    public async Task DisposeAsync() => await _dataSource.DisposeAsync();

    [Fact]
    public async Task ProcessOnce_ARedeliveredMessage_RunsTheWorkOnce()
    {
        var inbox = CreateInbox();
        var runs = 0;

        var first = await inbox.ProcessOnceAsync("consumer", "message-1", (_, _) => { runs++; return Task.CompletedTask; }, CancellationToken.None);
        var redelivery = await inbox.ProcessOnceAsync("consumer", "message-1", (_, _) => { runs++; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(first);
        Assert.False(redelivery);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task ProcessOnce_WhenTheWorkFails_LeavesNoClaimSoTheRetryRuns()
    {
        var inbox = CreateInbox();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            inbox.ProcessOnceAsync("consumer", "message-2", (_, _) => throw new TimeoutException("downstream"), CancellationToken.None));
        var retried = await inbox.ProcessOnceAsync("consumer", "message-2", (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.True(retried);
    }

    [Fact]
    public async Task ProcessOnce_TheSameMessageForTwoConsumers_RunsForEach()
    {
        var inbox = CreateInbox();

        Assert.True(await inbox.ProcessOnceAsync("email", "message-3", (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.True(await inbox.ProcessOnceAsync("inventory", "message-3", (_, _) => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessOnce_ConcurrentDeliveries_RunTheWorkOnce()
    {
        var inbox = CreateInbox();
        var runs = 0;

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            inbox.ProcessOnceAsync("consumer", "message-4", async (_, ct) =>
            {
                Interlocked.Increment(ref runs);
                await Task.Delay(200, ct);
            }, CancellationToken.None)));

        Assert.Equal(1, runs);
        Assert.Single(results, processed => processed);
    }

    [Fact]
    public async Task ProcessOnce_WorkWritesThroughTheInboxTransaction_CommitWithTheClaim()
    {
        var inbox = CreateInbox();

        await Assert.ThrowsAsync<TimeoutException>(() => inbox.ProcessOnceAsync("consumer", "message-5", async (tx, ct) =>
        {
            await using var write = new NpgsqlCommand("INSERT INTO inbox_messages (consumer, message_id) VALUES ('projection', 'message-5')", tx!.Connection, tx.Transaction);
            await write.ExecuteNonQueryAsync(ct);
            throw new TimeoutException("fails after writing");
        }, CancellationToken.None));

        await using var count = _dataSource.CreateCommand("SELECT count(*) FROM inbox_messages WHERE message_id = 'message-5'");
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    private PostgresMessageInbox CreateInbox() =>
        new(_dataSource, retentionDays: 7, TimeProvider.System, NullLogger<PostgresMessageInbox>.Instance);
}
