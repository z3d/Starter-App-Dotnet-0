using DbUp.Engine;
using Microsoft.Extensions.Configuration;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class MigrationTransactionTests
{
    private static readonly MigrationSessionOptions ShortTimeouts = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

    private readonly ApiTestFixture _fixture;

    public MigrationTransactionTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AFailingScript_LeavesTheScriptsBeforeItApplied()
    {
        var database = ScratchDatabase();

        var migrated = DatabaseMigrationEngine.MigrateDatabase(database,
        [
            new SqlScript("0001_Expand_First.sql", "CREATE TABLE first_table (id int);"),
            new SqlScript("0002_Expand_Broken.sql", "CREATE TABLE broken (id nonexistent_type);"),
        ], ShortTimeouts);

        Assert.False(migrated);
        Assert.Equal(["0001_Expand_First.sql"], await JournalAsync(database));
        Assert.True(await ExistsAsync(database, "SELECT to_regclass('first_table') IS NOT NULL"));
    }

    [Fact]
    public async Task ANoTransactionScript_CreatesAnIndexConcurrently_BetweenTransactionalOnes()
    {
        var database = ScratchDatabase();

        var migrated = DatabaseMigrationEngine.MigrateDatabase(database,
        [
            new SqlScript("0001_Expand_Table.sql", "CREATE TABLE people (id int, phone text);"),
            new SqlScript("0002_Expand_PhoneIndex.sql", DatabaseMigrationEngine.NoTransactionHeader + "\nCREATE INDEX CONCURRENTLY ix_people_phone ON people (phone);"),
            new SqlScript("0003_Expand_Column.sql", "ALTER TABLE people ADD COLUMN email text NULL;"),
        ], ShortTimeouts);

        Assert.True(migrated);
        Assert.Equal(["0001_Expand_Table.sql", "0002_Expand_PhoneIndex.sql", "0003_Expand_Column.sql"], await JournalAsync(database));
        Assert.True(await ExistsAsync(database, "SELECT to_regclass('ix_people_phone') IS NOT NULL"));
    }

    [Fact]
    public async Task AScriptWaitingOnALockHeldByTraffic_GivesUpAfterTheLockTimeout()
    {
        var database = ScratchDatabase();
        Assert.True(DatabaseMigrationEngine.MigrateDatabase(database, [new SqlScript("0001_Expand_Table.sql", "CREATE TABLE busy (id int);")], ShortTimeouts));

        await using var dataSource = NpgsqlDataSource.Create(database);
        await using var traffic = await dataSource.OpenConnectionAsync();
        await using var transaction = await traffic.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("LOCK TABLE busy IN ACCESS EXCLUSIVE MODE", traffic, transaction))
            await hold.ExecuteNonQueryAsync();

        var migration = Task.Run(() => DatabaseMigrationEngine.MigrateDatabase(database,
        [
            new SqlScript("0001_Expand_Table.sql", "CREATE TABLE busy (id int);"),
            new SqlScript("0002_Expand_Column.sql", "ALTER TABLE busy ADD COLUMN note text NULL;"),
        ], ShortTimeouts));

        Assert.False(await migration.WaitAsync(TimeSpan.FromSeconds(30)), "the migration should give up on the lock and report failure");
    }

    [Fact]
    public async Task AScriptRunningPastTheStatementTimeout_Fails()
    {
        var database = ScratchDatabase();

        var migration = Task.Run(() => DatabaseMigrationEngine.MigrateDatabase(database,
            [new SqlScript("0001_Expand_Slow.sql", "SELECT pg_sleep(20);")], ShortTimeouts));

        Assert.False(await migration.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void TheTimeouts_AreReadFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Migrator:LockTimeout"] = "00:00:30", ["Migrator:StatementTimeout"] = "01:00:00" })
            .Build();

        Assert.Equal(new MigrationSessionOptions(TimeSpan.FromSeconds(30), TimeSpan.FromHours(1)), MigrationSessionOptions.From(configuration));
        Assert.Equal(MigrationSessionOptions.Default, MigrationSessionOptions.From(new ConfigurationBuilder().Build()));
    }

    private string ScratchDatabase() =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = "migration_" + Guid.NewGuid().ToString("N")[..12] }.ConnectionString;

    private static async Task<string[]> JournalAsync(string database)
    {
        await using var dataSource = NpgsqlDataSource.Create(database);
        await using var command = dataSource.CreateCommand("SELECT scriptname FROM schemaversions ORDER BY scriptname");
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return [.. names];
    }

    private static async Task<bool> ExistsAsync(string database, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(database);
        await using var command = dataSource.CreateCommand(sql);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
