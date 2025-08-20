using StarterApp.DbMigrator;
using System.Reflection;

namespace StarterApp.Api.Data;

/// <summary>
/// Provides database migration capabilities for the API application
/// using the shared DatabaseMigrationEngine from the DbMigrator project
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>
    /// Runs database migrations using the shared migration engine
    /// </summary>
    /// <param name="connectionString">The database connection string</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    public static bool MigrateDatabase(string connectionString)
    {
        // Get the DbMigrator assembly that contains the embedded SQL scripts
        var dbMigratorAssembly = typeof(DatabaseMigrationEngine).Assembly;
        
        // Use the shared DatabaseMigrationEngine with the correct assembly
        return DatabaseMigrationEngine.MigrateDatabase(connectionString, dbMigratorAssembly);
    }
}