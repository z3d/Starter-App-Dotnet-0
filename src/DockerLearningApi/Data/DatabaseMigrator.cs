using System.Reflection;
using DockerLearning.DbMigrator;

namespace DockerLearningApi.Data;

public static class DatabaseMigrator
{
    public static bool MigrateDatabase(string connectionString)
    {
        // Use the shared DatabaseMigrationEngine from the DbMigrator project
        // but pass the current assembly to use SQL scripts embedded in the API project
        return DatabaseMigrationEngine.MigrateDatabase(
            connectionString,
            Assembly.GetExecutingAssembly());
    }
}