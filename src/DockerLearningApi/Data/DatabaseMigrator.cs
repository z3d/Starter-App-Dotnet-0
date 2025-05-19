using DbUp;
using DbUp.Engine;
using System.Reflection;

namespace DockerLearningApi.Data;

public static class DatabaseMigrator
{
    public static bool MigrateDatabase(string connectionString)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        // Configure DbUp with our custom journal that handles existing PK constraints
        var connectionManager = new DbUp.SqlServer.SqlConnectionManager(connectionString);
        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .JournalTo(new CustomSqlTableJournal(
                () => connectionManager,
                () => new DbUp.Engine.Output.ConsoleUpgradeLog(),
                "dbo",
                "SchemaVersions"))
            .WithTransaction()
            .LogToConsole()
            .Build();

        DatabaseUpgradeResult result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Database migration successful!");
        Console.ResetColor();
        return true;
    }
}