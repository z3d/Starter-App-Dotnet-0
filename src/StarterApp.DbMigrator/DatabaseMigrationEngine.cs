namespace StarterApp.DbMigrator;

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

    public static bool Migrate(string connectionString)
    {
        return MigrateDatabase(connectionString, Assembly.GetExecutingAssembly());
    }
}
