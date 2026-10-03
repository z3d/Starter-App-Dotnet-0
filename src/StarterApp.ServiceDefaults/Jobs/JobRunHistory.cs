using Npgsql;

namespace StarterApp.ServiceDefaults.Jobs;

public sealed record LastRun(DateTimeOffset StartedOnUtc, string? CompletedOutcome, DateTimeOffset? UnfinishedSinceUtc = null);

public interface IJobRunHistory
{
    Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, DateTimeOffset>> WatchedSinceAsync(IReadOnlyList<string> jobNames, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public sealed class NoJobRunHistory : IJobRunHistory
{
    public Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, LastRun>?>(null);

    public Task<IReadOnlyDictionary<string, DateTimeOffset>> WatchedSinceAsync(IReadOnlyList<string> jobNames, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, DateTimeOffset>>(new Dictionary<string, DateTimeOffset>());
}

public sealed class NpgsqlJobRunHistory : IJobRunHistory
{
    private const string LastRunsSql = """
        WITH finished AS (
            SELECT job_name, max(started_on_utc) AS last_started_on_utc
            FROM job_runs
            WHERE job_name = ANY(@jobNames) AND completed_on_utc IS NOT NULL
            GROUP BY job_name
        )
        SELECT r.job_name,
               max(r.started_on_utc),
               (array_agg(r.outcome ORDER BY r.started_on_utc DESC) FILTER (WHERE r.completed_on_utc IS NOT NULL))[1],
               min(r.started_on_utc) FILTER (WHERE r.completed_on_utc IS NULL AND (f.last_started_on_utc IS NULL OR r.started_on_utc > f.last_started_on_utc))
        FROM job_runs r
        LEFT JOIN finished f ON f.job_name = r.job_name
        WHERE r.job_name = ANY(@jobNames)
        GROUP BY r.job_name
        """;

    private const string WatchedSinceSql = """
        INSERT INTO watched_jobs (job_name, watched_since_utc)
        SELECT unnest(@jobNames), @nowUtc
        ON CONFLICT (job_name) DO NOTHING;

        SELECT job_name, watched_since_utc FROM watched_jobs WHERE job_name = ANY(@jobNames);
        """;

    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlJobRunHistory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken)
    {
        var runs = new Dictionary<string, LastRun>();
        await using var connection = _dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(LastRunsSql, connection);
        command.Parameters.AddWithValue("jobNames", jobNames.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var outcome = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2);
            DateTimeOffset? unfinishedSince = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false);
            runs[reader.GetString(0)] = new LastRun(await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false), outcome, unfinishedSince);
        }

        return runs;
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> WatchedSinceAsync(IReadOnlyList<string> jobNames, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var since = new Dictionary<string, DateTimeOffset>();
        await using var connection = _dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(WatchedSinceSql, connection);
        command.Parameters.AddWithValue("jobNames", jobNames.ToArray());
        command.Parameters.AddWithValue("nowUtc", nowUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            since[reader.GetString(0)] = await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false);

        return since;
    }
}
