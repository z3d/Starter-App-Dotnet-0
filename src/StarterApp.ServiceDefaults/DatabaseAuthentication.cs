using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Npgsql;

// This file is also compiled into StarterApp.DbMigrator under the DBMIGRATOR symbol, like ConnectionStringDescriptor.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// No password means connect as the hosting identity with an Entra token. The user name comes from the connection string, or from the token when the string names none. Nothing else decides a database credential.
public static class DatabaseAuthentication
{
    // The server rejects a management-plane token, so the scope has to be this one.
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
            return !string.IsNullOrEmpty(builder.Host) && string.IsNullOrEmpty(builder.Password);
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
        if (!UsesManagedIdentity(connectionString))
            return new NpgsqlDataSourceBuilder(WithoutGssEncryption(connectionString)).Build();

        var tokenCredential = credential ?? new DefaultAzureCredential();
        var settings = new NpgsqlConnectionStringBuilder(WithoutGssEncryption(connectionString));
        if (string.IsNullOrEmpty(settings.Username))
        {
            // The user name is fixed for the life of the data source, so one token is fetched here rather than in the password provider.
            var token = tokenCredential.GetToken(new TokenRequestContext([TokenScope]), CancellationToken.None);
            settings.Username = UsernameFromToken(token.Token);
        }

        RequireEncryption(settings);
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        builder.UsePeriodicPasswordProvider(
            async (_, cancellationToken) =>
                (await tokenCredential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken)).Token,
            TokenRefreshInterval,
            TokenRetryInterval);

        return builder.Build();
    }

    // For DbUp, which has no password provider hook. The token becomes the password. It lasts about an hour and is never logged.
    public static async Task<string> ResolveForDirectUseAsync(
        string connectionString,
        TokenCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (!UsesManagedIdentity(connectionString))
            return WithoutGssEncryption(connectionString);

        var tokenCredential = credential ?? new DefaultAzureCredential();
        var token = await tokenCredential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
        var settings = new NpgsqlConnectionStringBuilder(WithoutGssEncryption(connectionString)) { Password = token.Token };
        if (string.IsNullOrEmpty(settings.Username))
            settings.Username = UsernameFromToken(token.Token);
        RequireEncryption(settings);
        return settings.ConnectionString;
    }

    // Npgsql's default of Prefer loads libgssapi. The chiseled images do not have it, and Npgsql complains on every new connection.
    internal static string WithoutGssEncryption(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (settings.GssEncryptionMode == GssEncryptionMode.Prefer)
            settings.GssEncryptionMode = GssEncryptionMode.Disable;
        return settings.ConnectionString;
    }

    // Npgsql's default of Prefer would retry without TLS after a refused handshake.
    private static void RequireEncryption(NpgsqlConnectionStringBuilder settings)
    {
        if (settings.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            settings.SslMode = SslMode.Require;
    }

    // A managed identity's name is only in the xms_mirid claim. This is the same lookup order Aspire.Azure.Npgsql uses.
    internal static string UsernameFromToken(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException("The database access token is not a JWT; a username cannot be derived from it. Name the user in the connection string.");

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

        foreach (var claim in new[] { "upn", "preferred_username" })
        {
            if (claims.RootElement.TryGetProperty(claim, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()))
                return value.GetString()!;
        }

        if (claims.RootElement.TryGetProperty("xms_mirid", out var resourceId) && resourceId.ValueKind == JsonValueKind.String)
        {
            var name = resourceId.GetString()!.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        throw new InvalidOperationException("The database access token names no principal (no upn, preferred_username or xms_mirid claim). Name the user in the connection string.");
    }
}
