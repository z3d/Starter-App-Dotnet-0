using StarterApp.DbMigrator;
using Serilog;

// Create configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

// Configure Serilog
var loggerConfig = new LoggerConfiguration()
    .WriteTo.Console();

// Add Seq sink if URL is provided
var seqUrl = configuration["SEQ_URL"] ?? configuration["SeqUrl"];
if (!string.IsNullOrEmpty(seqUrl))
{
    loggerConfig.WriteTo.Seq(seqUrl);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("Starting database migration process");

    // Get connection string from configuration
    // Use the same connection string priority logic as the API
    var databaseConnection = configuration.GetConnectionString("database");
    var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
    var sqlserverConnection = configuration.GetConnectionString("sqlserver");
    var defaultConnection = configuration.GetConnectionString("DefaultConnection");

    var connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection;

    if (string.IsNullOrEmpty(connectionString))
    {
        Log.Error("Connection string is not configured");
        Environment.Exit(-1);
    }

    // Log connection string with password masked for security
    var maskedConnectionString = MaskConnectionStringPassword(connectionString);
    Log.Information("Using database connection: {ConnectionString}", maskedConnectionString);

    // Use the DatabaseMigrationEngine to run migrations
    bool success = DatabaseMigrationEngine.Migrate(connectionString);
    
    if (success)
    {
        Log.Information("Database migration completed successfully");
        Environment.Exit(0);
    }
    else
    {
        Log.Error("Database migration failed");
        Environment.Exit(-1);
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Database migration process failed with an exception");
    Environment.Exit(-1);
}
finally
{
    Log.CloseAndFlush();
}

// Helper method to mask passwords in connection strings
static string MaskConnectionStringPassword(string connectionString)
{
    if (string.IsNullOrEmpty(connectionString))
        return connectionString;
        
    return System.Text.RegularExpressions.Regex.Replace(
        connectionString,
        @"(password|pwd)\s*=\s*[^;]+",
        "$1=***MASKED***",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}




