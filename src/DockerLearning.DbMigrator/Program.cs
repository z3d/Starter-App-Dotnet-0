using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;

namespace DockerLearning.DbMigrator;

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

        Console.WriteLine($"Starting database migration...");
        
        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .WithTransaction()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Database migration failed: {result.Error}");
            Console.ResetColor();
            return -1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Database migration completed successfully!");
        Console.ResetColor();
        return 0;
    }
}
