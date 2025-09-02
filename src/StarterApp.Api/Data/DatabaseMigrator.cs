using StarterApp.DbMigrator;

namespace StarterApp.Api.Data;
public static class DatabaseMigrator
{
    public static bool MigrateDatabase(string connectionString)
    {
        // Get the DbMigrator assembly that contains the embedded SQL scripts
        var dbMigratorAssembly = typeof(DatabaseMigrationEngine).Assembly;

        // Use the shared DatabaseMigrationEngine with the correct assembly
        return DatabaseMigrationEngine.MigrateDatabase(connectionString, dbMigratorAssembly);
    }
}



