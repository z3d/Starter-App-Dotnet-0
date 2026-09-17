namespace StarterApp.DbMigrator;

public static class DatabaseMigrationEngine
{
    public static bool MigrateDatabase(string connectionString, Assembly scriptsAssembly)
    {
        var upgradeLog = new SerilogUpgradeLog(Log.Logger);

        EnsureDatabase.For.PostgresqlDatabase(connectionString, upgradeLog);

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
