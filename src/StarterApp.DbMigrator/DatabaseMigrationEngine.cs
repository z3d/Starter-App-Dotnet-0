namespace StarterApp.DbMigrator;

/// <summary>
/// Provides database migration functionality that can be used both
/// by the standalone migrator and the API application
/// </summary>
public static class DatabaseMigrationEngine
{
    /// <summary>
    /// Runs database migrations using scripts from the specified assembly
    /// </summary>
    /// <param name="connectionString">The database connection string</param>
    /// <param name="scriptsAssembly">The assembly containing embedded SQL scripts</param>
    /// <returns>True if migration was successful, false otherwise</returns>
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

    /// <summary>
    /// Simplified method that uses the executing assembly by default for SQL scripts
    /// </summary>
    /// <param name="connectionString">The database connection string</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    public static bool Migrate(string connectionString)
    {
        return MigrateDatabase(connectionString, Assembly.GetExecutingAssembly());
    }
}