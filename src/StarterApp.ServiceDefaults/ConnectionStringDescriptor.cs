using Npgsql;

// Linked into StarterApp.DbMigrator with the DBMIGRATOR constant so the copy lands in that
// assembly's own namespace; the test project references both assemblies and must see one type.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// The only approved way to put a connection string in a log line. It parses the string and
// emits the host, port, database and user, so there is no password field to get wrong. The
// previous regex mask stopped at the first semicolon and leaked the rest of a quoted password.
// This file is also compiled into StarterApp.DbMigrator, which does not reference ServiceDefaults.
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
