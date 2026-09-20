using Azure.Core;
using Azure.Identity;
using Npgsql;

// Linked into StarterApp.DbMigrator with the DBMIGRATOR constant, like ConnectionStringDescriptor:
// the migrator does not reference ServiceDefaults, and the test project references both assemblies.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// A connection string that names a user but carries no password means "connect as the hosting
// identity": Azure Database for PostgreSQL accepts an Entra access token as the password, and
// Npgsql refreshes it on the interval below so a pooled connection never opens with a token about
// to expire. A connection string with a password is used as given.
//
// This is the only place a database credential is decided. Every long-running process takes its
// connections from the one NpgsqlDataSource that CreateDataSource builds (EF Core, Dapper and the
// job-run recorder share it); the migrator, which cannot hand DbUp a password provider, resolves the
// token once up front with ResolveForDirectUseAsync. A raw `new NpgsqlConnection(string)` in
// production code is banned (BannedSymbols.txt) because it cannot carry the token.
public static class DatabaseAuthentication
{
    // The audience Azure Database for PostgreSQL validates tokens against; a management-plane
    // token is rejected by the server.
    public const string TokenScope = "https://ossrdbms-aad.database.windows.net/.default";

    // Tokens last about an hour. Refresh well inside that, and retry a failed fetch quickly.
    internal static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(45);
    internal static readonly TimeSpan TokenRetryInterval = TimeSpan.FromSeconds(10);

    public static bool UsesManagedIdentity(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrEmpty(builder.Username) && string.IsNullOrEmpty(builder.Password);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return false;
        }
    }

    public static string Describe(string? connectionString) =>
        UsesManagedIdentity(connectionString) ? "managed identity (Entra token)" : "password";

    public static NpgsqlDataSource CreateDataSource(string connectionString, TokenCredential? credential = null)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);

        if (UsesManagedIdentity(connectionString))
        {
            var tokenCredential = credential ?? new DefaultAzureCredential();
            builder.UsePeriodicPasswordProvider(
                async (_, cancellationToken) =>
                    (await tokenCredential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken)).Token,
                TokenRefreshInterval,
                TokenRetryInterval);
        }

        return builder.Build();
    }

    // For a short-lived process that must hand a plain connection string to a library with no
    // password-provider hook (DbUp in the migrator): the token becomes the password. The result
    // is good for about an hour and must never be logged or stored; a string that already carries
    // a password is returned untouched and the credential is never consulted.
    public static async Task<string> ResolveForDirectUseAsync(
        string connectionString,
        TokenCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (!UsesManagedIdentity(connectionString))
            return connectionString;

        var tokenCredential = credential ?? new DefaultAzureCredential();
        var token = await tokenCredential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
        return new NpgsqlConnectionStringBuilder(connectionString) { Password = token.Token }.ConnectionString;
    }
}
