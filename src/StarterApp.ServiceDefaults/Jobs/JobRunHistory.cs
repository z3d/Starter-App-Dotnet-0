using Npgsql;

namespace StarterApp.ServiceDefaults.Jobs;

public sealed record LastRun(DateTimeOffset StartedOnUtc, string? CompletedOutcome);

public interface IJobRunHistory
{
    Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken);
}

public sealed class NoJobRunHistory : IJobRunHistory
{
    public Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, LastRun>?>(null);
}

public sealed class NpgsqlJobRunHistory : IJobRunHistory
{
    private const string Sql = """
        SELECT job_name,
               max(started_on_utc),
               (array_agg(outcome ORDER BY started_on_utc DESC) FILTER (WHERE completed_on_utc IS NOT NULL))[1]
        FROM job_runs
        WHERE job_name = ANY(@jobNames)
        GROUP BY job_name
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
        await using var command = new NpgsqlCommand(Sql, connection);
        command.Parameters.AddWithValue("jobNames", jobNames.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var outcome = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2);
            runs[reader.GetString(0)] = new LastRun(await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false), outcome);
        }

        return runs;
    }
}
