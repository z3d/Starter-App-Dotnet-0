namespace StarterApp.DbMigrator;

public static class DatabaseMigrationEngine
{
    public static bool MigrateDatabase(string connectionString, Assembly scriptsAssembly)
    {
        // Mask the connection string password for logging
        var maskedConnectionString = MaskConnectionStringPassword(connectionString);
        Console.WriteLine($"Starting database migration with connection: {maskedConnectionString}");

        // Ensure database exists
        EnsureDatabase.For.SqlDatabase(connectionString);

        // Configure DbUp to use standard journal (default "__SchemaVersions" table)
        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(scriptsAssembly)
            .WithTransaction()
            .LogToNowhere() // Don't log to console to avoid exposing connection strings
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

    public static bool Migrate(string connectionString)
    {
        return MigrateDatabase(connectionString, Assembly.GetExecutingAssembly());
    }

    private static string MaskConnectionStringPassword(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        return System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            @"(password|pwd)\s*=\s*[^;]+",
            "$1=***MASKED***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}



