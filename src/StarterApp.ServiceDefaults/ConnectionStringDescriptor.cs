using Npgsql;

// Linked into StarterApp.DbMigrator with the DBMIGRATOR constant so the copy lands in that
// assembly's own namespace; the test project references both assemblies and must see one type.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// The one sanctioned way to put a database connection in a log line. It never reconstructs the
// connection string: the previous regex mask stopped at the first semicolon, so a valid quoted
// password containing one leaked its suffix. Parsing structurally and emitting only host, port,
// database and user means there is no password field to get wrong. Linked into StarterApp.DbMigrator
// as a source file (the migrator deliberately does not reference ServiceDefaults).
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
