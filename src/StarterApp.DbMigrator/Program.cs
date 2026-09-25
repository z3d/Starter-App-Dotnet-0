using StarterApp.DbMigrator;

// The bare verb token is not key=value shaped and would fail AddCommandLine parsing.
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

// Returned exit codes let finally flush the Seq sink. A failure at about fifteen seconds is Npgsql's connect timeout; sub-second is login or SQL.
var elapsed = System.Diagnostics.Stopwatch.StartNew();

try
{
    Log.Information("Starting database migration process");

    var databaseConnection = configuration.GetConnectionString("database");
    var postgresConnection = configuration.GetConnectionString("postgres");
    var defaultConnection = configuration.GetConnectionString("DefaultConnection");

    var connectionString = databaseConnection ?? postgresConnection ?? defaultConnection;

    if (string.IsNullOrEmpty(connectionString))
    {
        Log.Error("Connection string is not configured");
        return -1;
    }

    Log.Information("Using database connection: {ConnectionString} ({DatabaseAuthentication})",
        ConnectionStringDescriptor.Describe(connectionString), DatabaseAuthentication.Describe(connectionString));

    // DbUp takes a plain string, so the token is resolved once here; never log the result.
    connectionString = await DatabaseAuthentication.ResolveForDirectUseAsync(connectionString);

    if (isReplayVerb)
    {
        return OutboxReplayer.Run(connectionString, args.Skip(1).ToArray());
    }

    if (DatabaseMigrationEngine.Migrate(connectionString))
    {
        Log.Information("Database migration completed successfully in {Elapsed}", elapsed.Elapsed);
        return 0;
    }

    Log.Error("Database migration failed after {Elapsed}", elapsed.Elapsed);
    return -1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Database migration failed after {Elapsed} with {ExceptionType}", elapsed.Elapsed, ex.GetType().Name);
    return -1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
