using Npgsql;

namespace StarterApp.DbMigrator;

public static class DatabaseMigrationEngine
{
    // Any constant works as long as every migrator run for this database uses the same one.
    public const long MigrationLockKey = 0x5374_6172_7465_724D;

    public static bool MigrateDatabase(string connectionString, Assembly scriptsAssembly)
    {
        var upgradeLog = new SerilogUpgradeLog(Log.Logger);

        EnsureDatabase.For.PostgresqlDatabase(connectionString, upgradeLog);

        // Overlapping runs (a retried deploy job) would both see the same scripts pending; the second waits here, then finds none.
        // The lock is session-scoped, so disposing the connection releases it even if the upgrade throws.
        using var dataSource = NpgsqlDataSource.Create(connectionString);
        using var lockConnection = dataSource.OpenConnection();
        Log.Information("Waiting for the migration lock");
        using (var acquire = lockConnection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(@key)";
            acquire.Parameters.AddWithValue("key", MigrationLockKey);
            acquire.ExecuteNonQuery();
        }

        Log.Information("Migration lock acquired");

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(scriptsAssembly)
            .WithTransaction()
            .LogTo(upgradeLog)
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Log.Error(result.Error, "Database migration failed while running {Script}", result.ErrorScript?.Name ?? "<no script>");
            return false;
        }

        Log.Information("Database migration applied {ScriptCount} script(s)", result.Scripts.Count());
        return true;
    }

    public static bool Migrate(string connectionString)
    {
        return MigrateDatabase(connectionString, Assembly.GetExecutingAssembly());
    }
}
