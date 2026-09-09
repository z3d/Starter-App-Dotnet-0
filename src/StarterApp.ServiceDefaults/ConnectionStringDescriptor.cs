using Npgsql;

// Linked into StarterApp.DbMigrator with the DBMIGRATOR constant so the copy lands in that
// assembly's own namespace; the test project references both assemblies and must see one type.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// Log only parsed host, port, database, and user fields. Masking a raw connection string
// can leak passwords containing quoted semicolons.
// Also compiled into StarterApp.DbMigrator, which does not reference ServiceDefaults.
public static class ConnectionStringDescriptor
{
    public static string Describe(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "<not configured>";

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"Host={builder.Host};Port={builder.Port};Database={builder.Database};Username={builder.Username}";
        }
        catch (Exception)
        {
            // Never echo the raw value on a parse failure — that is exactly when a secret is
            // most likely to be sitting in an unexpected position.
            return "<unparseable connection string>";
        }
    }
}
