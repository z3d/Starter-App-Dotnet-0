using StarterApp.DbMigrator;

// Ops verbs (currently: replay-outbox) bypass AddCommandLine — the bare verb token
// is not key=value shaped and would fail configuration parsing. Their arguments are
// parsed explicitly by the verb handler instead.
var isReplayVerb = args.Length > 0 && string.Equals(args[0], "replay-outbox", StringComparison.OrdinalIgnoreCase);
var configurationArgs = isReplayVerb ? Array.Empty<string>() : args;

// Create configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(configurationArgs)
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

// Every path returns an exit code instead of calling Environment.Exit so the finally block can
// flush the Seq sink; Environment.Exit terminates before finally runs and a short migration or
// replay would exit with its whole log batch undelivered.
try
{
    Log.Information("Starting database migration process");

    // Get connection string from configuration
    // Use the same connection string priority logic as the API
    var databaseConnection = configuration.GetConnectionString("database");
    var postgresConnection = configuration.GetConnectionString("postgres");
    var defaultConnection = configuration.GetConnectionString("DefaultConnection");

    var connectionString = databaseConnection ?? postgresConnection ?? defaultConnection;

    if (string.IsNullOrEmpty(connectionString))
    {
        Log.Error("Connection string is not configured");
        return -1;
    }

    Log.Information("Using database connection: {ConnectionString}", ConnectionStringDescriptor.Describe(connectionString));

    if (isReplayVerb)
    {
        return OutboxReplayer.Run(connectionString, args.Skip(1).ToArray());
    }

    // Use the DatabaseMigrationEngine to run migrations
    bool success = DatabaseMigrationEngine.Migrate(connectionString);

    if (success)
    {
        Log.Information("Database migration completed successfully");
        return 0;
    }
    else
    {
        Log.Error("Database migration failed");
        return -1;
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Database migration process failed with an exception");
    return -1;
}
finally
{
    Log.CloseAndFlush();
}
