using Npgsql;

// Also compiled into StarterApp.DbMigrator under DBMIGRATOR; the test project must see one type per assembly.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// The only approved way to log a connection string; the previous regex mask leaked the rest of a quoted password.
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
            // A parse failure is exactly when a secret sits somewhere unexpected; never echo the raw value.
            return "<unparseable connection string>";
        }
    }
}
