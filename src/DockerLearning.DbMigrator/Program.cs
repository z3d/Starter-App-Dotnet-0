using Microsoft.Extensions.Configuration;
using DockerLearning.DbMigrator;

// Create configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

// Get connection string from configuration
// Try Aspire-provided connection string first, then fall back to DefaultConnection
var connectionString = configuration.GetConnectionString("DockerLearning") 
                       ?? configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Connection string is not configured.");
    Console.ResetColor();
    Environment.Exit(-1);
}

// Use the DatabaseMigrationEngine to run migrations
bool success = DatabaseMigrationEngine.Migrate(connectionString);

Environment.Exit(success ? 0 : -1);
