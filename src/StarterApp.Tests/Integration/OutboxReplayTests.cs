namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class OutboxReplayTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OutboxReplayTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("Resetting database for outbox replay test");
        await _fixture.ResetDatabaseAsync();
    }

    public async Task DisposeAsync() => await Task.CompletedTask;

    [Fact]
    public async Task ReplayVerb_ResetsErroredRow_EndToEnd()
    {
        var messageId = await InsertOutboxMessageAsync(markErrored: true);

        var exitCode = OutboxReplayer.Run(_fixture.ConnectionString, ["--id", messageId.ToString()]);

        Assert.Equal(0, exitCode);

        var row = await LoadAsync(messageId);
        Assert.Null(row.Error);
        Assert.Null(row.ProcessedOnUtc);
        Assert.Null(row.ProcessingId);
        Assert.Null(row.LockedUntilUtc);
        Assert.Equal(0, row.RetryCount);
        Assert.Equal(1, row.ReplayCount);
        Assert.NotNull(row.ReplayedOnUtc);
    }

    [Fact]
    public async Task ReplayVerb_RefusesProcessedRow()
    {
        var messageId = await InsertOutboxMessageAsync(markErrored: false, markProcessed: true);

        var exitCode = OutboxReplayer.Run(_fixture.ConnectionString, ["--id", messageId.ToString()]);

        Assert.Equal(1, exitCode);

        var row = await LoadAsync(messageId);
        Assert.NotNull(row.ProcessedOnUtc);
        Assert.Equal(0, row.ReplayCount);
        Assert.Null(row.ReplayedOnUtc);
    }

    [Fact]
    public async Task ReplayVerb_AllErrored_ResetsEveryErroredRowOnly()
    {
        var errored1 = await InsertOutboxMessageAsync(markErrored: true);
        var errored2 = await InsertOutboxMessageAsync(markErrored: true);
        var pending = await InsertOutboxMessageAsync(markErrored: false);
        var processed = await InsertOutboxMessageAsync(markErrored: false, markProcessed: true);

        var exitCode = OutboxReplayer.Run(_fixture.ConnectionString, ["--all-errored"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, (await LoadAsync(errored1)).ReplayCount);
        Assert.Equal(1, (await LoadAsync(errored2)).ReplayCount);
        Assert.Equal(0, (await LoadAsync(pending)).ReplayCount);
        Assert.Equal(0, (await LoadAsync(processed)).ReplayCount);
    }

    [Fact]
    public void ReplayVerb_RejectsAmbiguousOrMissingArguments()
    {
        Assert.Equal(2, OutboxReplayer.Run(_fixture.ConnectionString, []));
        Assert.Equal(2, OutboxReplayer.Run(_fixture.ConnectionString, ["--id", Guid.NewGuid().ToString(), "--all-errored"]));
        Assert.Equal(2, OutboxReplayer.Run(_fixture.ConnectionString, ["--bogus"]));
    }

    [Fact]
    public async Task SqlReplay_MatchesEntityResetForReplaySemantics()
    {
        // The verb's SQL and OutboxMessage.ResetForReplay are dual representations
        // of the same reset. Drift between them must fail here.
        var sqlResetId = await InsertOutboxMessageAsync(markErrored: true);
        OutboxReplayer.Run(_fixture.ConnectionString, ["--id", sqlResetId.ToString()]);
        var viaSql = await LoadAsync(sqlResetId);

        var entityResetId = await InsertOutboxMessageAsync(markErrored: true);
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = await dbContext.Set<OutboxMessage>().SingleAsync(m => m.Id == entityResetId);
            entity.ResetForReplay(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        var viaEntity = await LoadAsync(entityResetId);

        Assert.Equal(viaSql.Error, viaEntity.Error);
        Assert.Equal(viaSql.RetryCount, viaEntity.RetryCount);
        Assert.Equal(viaSql.ReplayCount, viaEntity.ReplayCount);
        Assert.Equal(viaSql.ProcessingId, viaEntity.ProcessingId);
        Assert.Equal(viaSql.LockedUntilUtc, viaEntity.LockedUntilUtc);
        Assert.NotNull(viaEntity.ReplayedOnUtc);
    }

    private async Task<Guid> InsertOutboxMessageAsync(bool markErrored, bool markProcessed = false)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = new Order(Guid.CreateVersion7(), 42, "replay-owner", "replay-tenant");
        order.AddItem(7, "Replay Product", 1, Money.Create(10m, "USD"));
        var message = OutboxMessage.Create(new OrderCreatedDomainEvent(order));

        if (markErrored)
            message.MarkAsError("intentional test failure");
        if (markProcessed)
            message.MarkAsProcessed(DateTimeOffset.UtcNow);

        dbContext.Set<OutboxMessage>().Add(message);
        await dbContext.SaveChangesAsync();
        return message.Id;
    }

    private async Task<OutboxMessage> LoadAsync(Guid id)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id);
    }
}
