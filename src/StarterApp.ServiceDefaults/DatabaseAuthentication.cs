using System.Text;
using System.Text.Json;
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

// A connection string with no password means "connect as the hosting identity": Azure Database
// for PostgreSQL accepts an Entra access token as the password, and Npgsql refreshes it on the
// interval below so a pooled connection never opens with a token about to expire. The user is the
// one named in the string, or — when the string names none, which is the shape Aspire emits for an
// Entra-only flexible server — the identity the token was issued to (its upn, preferred_username
// or, for a managed identity, the name in xms_mirid), the same derivation Aspire's own Npgsql
// integration performs. TLS is required on that path so a rejected token can never be retried in
// the clear. A connection string with a password is used as given.
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
            // The user is fixed for the life of the data source, so it is derived from one token
            // here rather than in the password provider.
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
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { Password = token.Token };
        if (string.IsNullOrEmpty(settings.Username))
            settings.Username = UsernameFromToken(token.Token);
        RequireEncryption(settings);
        return settings.ConnectionString;
    }

    // Npgsql's default (Prefer) would retry in the clear after a refused TLS handshake, and a
    // token must never travel unencrypted. An explicit stricter mode in the string is kept.
    private static void RequireEncryption(NpgsqlConnectionStringBuilder settings)
    {
        if (settings.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            settings.SslMode = SslMode.Require;
    }

    // Azure Database for PostgreSQL matches the token to a principal by name: a user's upn or
    // preferred_username, or a managed identity's name, which the token carries only inside
    // xms_mirid (".../userAssignedIdentities/<name>"). Same lookup order as Aspire.Azure.Npgsql.
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
