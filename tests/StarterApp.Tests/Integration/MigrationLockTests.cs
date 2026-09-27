namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class MigrationLockTests
{
    private readonly ApiTestFixture _fixture;

    public MigrationLockTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrate_WhenAnotherRunHoldsTheLock_WaitsForItThenSucceeds()
    {
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var otherRun = await dataSource.OpenConnectionAsync();
        await using (var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", otherRun))
        {
            acquire.Parameters.AddWithValue("key", DatabaseMigrationEngine.MigrationLockKey);
            await acquire.ExecuteNonQueryAsync();
        }

        var migration = Task.Run(() => DatabaseMigrationEngine.Migrate(_fixture.ConnectionString));

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(migration.IsCompleted, "The migrator must wait while another run holds the migration lock.");

        await using (var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", otherRun))
        {
            release.Parameters.AddWithValue("key", DatabaseMigrationEngine.MigrationLockKey);
            await release.ExecuteNonQueryAsync();
        }

        Assert.True(await migration.WaitAsync(TimeSpan.FromSeconds(30)));
    }
}
