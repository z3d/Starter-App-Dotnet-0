using Azure.Core;
using Azure.Identity;
using Npgsql;

namespace StarterApp.Api.Infrastructure.Persistence;

// A connection string that names a user but carries no password means "connect as the hosting
// identity": Azure Database for PostgreSQL accepts an Entra access token as the password, and
// Npgsql refreshes it on the interval below so a pooled connection never opens with a token about
// to expire. A connection string with a password is used as given. This covers the API's data
// source (EF Core and Dapper share it); the migrator connects with whatever string it is handed.
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
}
