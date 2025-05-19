using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;

namespace DockerLearning.DbMigrator;

// Shared migration class that can be used by both the migrator and the API
public static class DatabaseMigrationEngine
{
    public static bool MigrateDatabase(string connectionString, Assembly scriptsAssembly)
    {
        Console.WriteLine($"Starting database migration...");
        
        // Ensure database exists
        EnsureDatabase.For.SqlDatabase(connectionString);
        
        // Configure DbUp to use standard journal (default "__SchemaVersions" table)
        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(scriptsAssembly)
            .WithTransaction()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Database migration failed: {result.Error}");
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Database migration completed successfully!");
        Console.ResetColor();
        return true;
    }
}

class Program
{
    static int Main(string[] args)
    {
        // Create configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        // Get connection string from configuration
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Connection string is not configured.");
            Console.ResetColor();
            return -1;
        }

        // Use the shared migration engine
        bool success = DatabaseMigrationEngine.MigrateDatabase(
            connectionString, 
            Assembly.GetExecutingAssembly());

        return success ? 0 : -1;
    }
}
