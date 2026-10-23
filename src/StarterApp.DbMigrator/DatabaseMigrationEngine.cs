using System.Globalization;
using System.Text;
using DbUp.Engine;
using Npgsql;

namespace StarterApp.DbMigrator;

public sealed record MigrationSessionOptions(TimeSpan LockTimeout, TimeSpan StatementTimeout)
{
    public static MigrationSessionOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(15));

    public static MigrationSessionOptions From(IConfiguration configuration) => new(
        Read(configuration, "Migrator:LockTimeout", Default.LockTimeout),
        Read(configuration, "Migrator:StatementTimeout", Default.StatementTimeout));

    private static TimeSpan Read(IConfiguration configuration, string key, TimeSpan fallback)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : throw new InvalidOperationException($"{key} must be a positive time span such as 00:00:05, not '{value}'.");
    }
}

public static class DatabaseMigrationEngine
{
    // Any number works, as long as every migrator run against this database uses the same one.
    public const long MigrationLockKey = 0x5374_6172_7465_724D;

    public const string NoTransactionHeader = "-- migrator: no-transaction";

    public static bool MigrateDatabase(string connectionString, Assembly scriptsAssembly, MigrationSessionOptions? session = null) =>
        MigrateDatabase(connectionString, EmbeddedScripts(scriptsAssembly), session);

    public static bool MigrateDatabase(string connectionString, IReadOnlyList<SqlScript> scripts, MigrationSessionOptions? session = null)
    {
        var upgradeLog = new SerilogUpgradeLog(Log.Logger);

        EnsureDatabase.For.PostgresqlDatabase(connectionString, upgradeLog);

        // Two runs at once (a retried deploy job) would both see the same scripts pending. The second waits here, then finds nothing to do.
        // The lock belongs to the session, so closing the connection releases it even if the upgrade throws.
        using var dataSource = NpgsqlDataSource.Create(connectionString);
        using var lockConnection = dataSource.OpenConnection();
        Log.Information("Waiting for the migration lock");
        using (var acquire = lockConnection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(@key)";
            acquire.Parameters.AddWithValue("key", MigrationLockKey);
            acquire.ExecuteNonQuery();
        }

        Log.Information("Migration lock acquired");

        session ??= MigrationSessionOptions.Default;
        var scriptConnectionString = WithSessionTimeouts(connectionString, session);
        var applied = 0;

        foreach (var segment in Segments(scripts))
        {
            var builder = DeployChanges.To
                .PostgresqlDatabase(scriptConnectionString)
                .WithScripts(segment.Scripts)
                .LogTo(upgradeLog);
            builder = segment.InTransaction ? builder.WithTransactionPerScript() : builder.WithoutTransaction();

            var result = builder.Build().PerformUpgrade();
            applied += result.Scripts.Count();

            if (!result.Successful)
            {
                Log.Error(result.Error,
                    "Database migration failed while running {Script}; the scripts before it are applied. A lock wait over {LockTimeout} or a statement over {StatementTimeout} fails the script (Migrator:LockTimeout, Migrator:StatementTimeout)",
                    result.ErrorScript?.Name ?? "<no script>", session.LockTimeout, session.StatementTimeout);
                return false;
            }
        }

        Log.Information("Database migration applied {ScriptCount} script(s)", applied);
        return true;
    }

    public static bool Migrate(string connectionString, MigrationSessionOptions? session = null)
    {
        return MigrateDatabase(connectionString, Assembly.GetExecutingAssembly(), session);
    }

    public static bool RunsOutsideATransaction(string sql) =>
        sql.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith(NoTransactionHeader, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<SqlScript> EmbeddedScripts(Assembly assembly) =>
        assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                return SqlScript.FromStream(name, stream, Encoding.UTF8);
            })
            .ToList();

    private static string WithSessionTimeouts(string connectionString, MigrationSessionOptions session)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var timeouts = string.Create(CultureInfo.InvariantCulture,
            $"-c lock_timeout={(long)session.LockTimeout.TotalMilliseconds} -c statement_timeout={(long)session.StatementTimeout.TotalMilliseconds}");
        builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? timeouts : $"{builder.Options} {timeouts}";
        return builder.ConnectionString;
    }

    private static IEnumerable<(bool InTransaction, List<SqlScript> Scripts)> Segments(IReadOnlyList<SqlScript> scripts)
    {
        var current = new List<SqlScript>();
        var inTransaction = true;
        foreach (var script in scripts.OrderBy(script => script.Name, StringComparer.Ordinal))
        {
            var scriptInTransaction = !RunsOutsideATransaction(script.Contents);
            if (current.Count > 0 && scriptInTransaction != inTransaction)
            {
                yield return (inTransaction, current);
                current = [];
            }

            inTransaction = scriptInTransaction;
            current.Add(script);
        }

        if (current.Count > 0)
            yield return (inTransaction, current);
    }
}
