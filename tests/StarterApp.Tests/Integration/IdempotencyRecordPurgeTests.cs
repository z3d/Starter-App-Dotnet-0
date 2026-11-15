using StarterApp.Functions;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class IdempotencyRecordPurgeTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private NpgsqlDataSource _dataSource = null!;

    public IdempotencyRecordPurgeTests(ApiTestFixture fixture)
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
    public async Task RecordsOlderThanTheCutoffGo_AndNewerOnesStay()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, ageDays) in new[] { ("old-1", 40), ("old-2", 31), ("new-1", 1) })
        {
            await using var insert = _dataSource.CreateCommand("""
                INSERT INTO idempotency_records (tenant_id, owner_subject, operation, idempotency_key, request_hash, resource_id, created_on_utc)
                VALUES ('tenant', 'subject', 'orders.create', @key, 'hash', 'resource', @createdOnUtc)
                """);
            insert.Parameters.AddWithValue("key", key);
            insert.Parameters.AddWithValue("createdOnUtc", now.AddDays(-ageDays));
            await insert.ExecuteNonQueryAsync();
        }

        var result = await new IdempotencyRecordPurge(_dataSource).DeleteOlderThanAsync(now.AddDays(-30), TimeSpan.FromMinutes(1), TimeProvider.System, CancellationToken.None);

        Assert.Equal(2, result.Deleted);
        Assert.False(result.BudgetExhausted);
        await using var left = _dataSource.CreateCommand("SELECT idempotency_key FROM idempotency_records");
        Assert.Equal("new-1", await left.ExecuteScalarAsync());
    }

    [Fact]
    public async Task WithNoDatabase_ThereIsNothingToPurge()
    {
        var result = await new IdempotencyRecordPurge(null).DeleteOlderThanAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), TimeProvider.System, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
    }
}
