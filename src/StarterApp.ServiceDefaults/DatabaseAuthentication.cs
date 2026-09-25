using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Npgsql;

// Also compiled into StarterApp.DbMigrator under DBMIGRATOR, like ConnectionStringDescriptor.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// No password means connect as the hosting identity with an Entra token; the user comes from the string or, when it names none, from the token. This is the only place a database credential is decided.
public static class DatabaseAuthentication
{
    // A management-plane token is rejected by the server.
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
            return new NpgsqlDataSourceBuilder(connectionString).Build();

        var tokenCredential = credential ?? new DefaultAzureCredential();
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrEmpty(settings.Username))
        {
            // The user is fixed for the life of the data source, so one token here rather than in the password provider.
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

    // For DbUp, which has no password-provider hook: the token becomes the password, good for about an hour, never logged.
    public static async Task<string> ResolveForDirectUseAsync(
        string connectionString,
        TokenCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (!UsesManagedIdentity(connectionString))
            return connectionString;

        var tokenCredential = credential ?? new DefaultAzureCredential();
        var token = await tokenCredential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { Password = token.Token };
        if (string.IsNullOrEmpty(settings.Username))
            settings.Username = UsernameFromToken(token.Token);
        RequireEncryption(settings);
        return settings.ConnectionString;
    }

    // Npgsql's default (Prefer) would retry in the clear after a refused TLS handshake.
    private static void RequireEncryption(NpgsqlConnectionStringBuilder settings)
    {
        if (settings.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            settings.SslMode = SslMode.Require;
    }

    // A managed identity's name is only inside xms_mirid; same lookup order as Aspire.Azure.Npgsql.
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
