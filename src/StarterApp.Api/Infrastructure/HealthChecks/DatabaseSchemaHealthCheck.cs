using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace StarterApp.Api.Infrastructure.HealthChecks;

public sealed class DatabaseSchemaHealthCheck : IHealthCheck
{
    private const string ResourcePrefix = "migrations/";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IReadOnlyList<string> _expectedScripts;
    private volatile bool _upToDate;

    public DatabaseSchemaHealthCheck(NpgsqlDataSource dataSource)
        : this(dataSource, EmbeddedScriptNames(typeof(DatabaseSchemaHealthCheck).Assembly))
    {
    }

    internal DatabaseSchemaHealthCheck(NpgsqlDataSource dataSource, IReadOnlyList<string> expectedScripts)
    {
        _dataSource = dataSource;
        _expectedScripts = expectedScripts;
    }

    internal static IReadOnlyList<string> EmbeddedScriptNames(Assembly assembly) =>
        assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..].Replace('\\', '.').Replace('/', '.'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_upToDate)
            return HealthCheckResult.Healthy("The database has every migration this build expects");

        try
        {
            var applied = new List<string>();
            await using (var command = _dataSource.CreateCommand("SELECT scriptname FROM schemaversions"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    applied.Add(reader.GetString(0));
            }

            var missing = _expectedScripts
                .Where(expected => !applied.Any(name => name.EndsWith("." + expected, StringComparison.Ordinal)))
                .ToList();
            if (missing.Count == 0)
            {
                _upToDate = true;
                return HealthCheckResult.Healthy("The database has every migration this build expects");
            }

            return HealthCheckResult.Unhealthy(
                $"The database is behind this build: {missing.Count} migration(s) not applied, the first {missing[0]}. Run the migrator before this revision takes traffic.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return HealthCheckResult.Unhealthy("The database has no migration journal. Run the migrator before this revision takes traffic.");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("The migration journal could not be read", ex);
        }
    }
}
