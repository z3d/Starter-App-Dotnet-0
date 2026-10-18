using Npgsql;

namespace StarterApp.DbMigrator;

public static class DatabaseMigrationEngine
{
    // Any number works, as long as every migrator run against this database uses the same one.
    public const long MigrationLockKey = 0x5374_6172_7465_724D;

    public static bool MigrateDatabase(string connectionString, Assembly scriptsAssembly)
    {
        var upgradeLog = new SerilogUpgradeLog(Log.Logger);

        EnsureDatabase.For.PostgresqlDatabase(connectionString, upgradeLog);

        // Two runs at once (a retried deploy job) would both see the same scripts pending. The second waits here, then finds nothing to do.
        // The lock belongs to the session, so closing the connection releases it even if the upgrade throws.
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
