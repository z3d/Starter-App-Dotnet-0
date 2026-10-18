using Npgsql;

// This file is also compiled into StarterApp.DbMigrator under the DBMIGRATOR symbol. The tests need one copy of the type per assembly.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// The only approved way to log a connection string. The regex mask it replaced leaked part of a quoted password.
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
            // Never echo the raw value when parsing fails. That is exactly when a secret is sitting somewhere unexpected.
            return "<unparseable connection string>";
        }
    }
}
