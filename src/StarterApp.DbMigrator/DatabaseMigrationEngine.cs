namespace StarterApp.DbMigrator;

public static class DatabaseMigrationEngine
{
    public static bool MigrateDatabase(string connectionString, Assembly scriptsAssembly)
    {
        Console.WriteLine($"Starting database migration with connection: {ConnectionStringDescriptor.Describe(connectionString)}");

        // Ensure database exists
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        // Configure DbUp to use standard journal (default "__SchemaVersions" table)
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
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
}
