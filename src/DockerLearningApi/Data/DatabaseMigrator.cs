using DbUp;
using DbUp.Engine;
using System.Reflection;

namespace DockerLearningApi.Data;

public static class DatabaseMigrator
{
    public static bool MigrateDatabase(string connectionString)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        // Configure DbUp with standard journal (uses default "__SchemaVersions" table)
        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
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