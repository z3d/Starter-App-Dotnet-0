using Microsoft.Extensions.Logging;
using Npgsql;

namespace StarterApp.ServiceDefaults.Jobs;

// Durable run history for background work (the "what did the background work actually do"
// trail). Telemetry sidecar semantics: recording failures are logged and swallowed — a
// history write must never take down the job it describes. Rows land in job_runs
// (migration 0003) and age out after JobRuns:RetentionDays (default 30).
public interface IJobRunRecorder
{
    Task<Guid> StartRunAsync(string jobName, DateTimeOffset startedOnUtc, CancellationToken cancellationToken);
    Task CompleteRunAsync(Guid runId, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken);
    Task RecordRunAsync(string jobName, DateTimeOffset startedOnUtc, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken);
}

public sealed class NullJobRunRecorder : IJobRunRecorder
{
    public Task<Guid> StartRunAsync(string jobName, DateTimeOffset startedOnUtc, CancellationToken cancellationToken) => Task.FromResult(Guid.Empty);
    public Task CompleteRunAsync(Guid runId, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RecordRunAsync(string jobName, DateTimeOffset startedOnUtc, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NpgsqlJobRunRecorder : IJobRunRecorder
{
    private readonly string _connectionString;
    private readonly int _retentionDays;
    private readonly ILogger<NpgsqlJobRunRecorder> _logger;
    private DateTimeOffset _lastPurgeUtc;

    public NpgsqlJobRunRecorder(string connectionString, int retentionDays, ILogger<NpgsqlJobRunRecorder> logger)
    {
        _connectionString = connectionString;
        _retentionDays = retentionDays;
        _logger = logger;
    }

    public async Task<Guid> StartRunAsync(string jobName, DateTimeOffset startedOnUtc, CancellationToken cancellationToken)
    {
        var runId = Guid.CreateVersion7();
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                "INSERT INTO job_runs (id, job_name, started_on_utc) VALUES (@id, @jobName, @startedOnUtc)", connection);
            command.Parameters.AddWithValue("id", runId);
            command.Parameters.AddWithValue("jobName", jobName);
            command.Parameters.AddWithValue("startedOnUtc", startedOnUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await PurgeIfDueAsync(connection, cancellationToken).ConfigureAwait(false);
            return runId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to record job run start for {JobName}; the job continues without run history", jobName);
            return Guid.Empty;
        }
    }

    public async Task CompleteRunAsync(Guid runId, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken)
    {
        if (runId == Guid.Empty)
            return;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                "UPDATE job_runs SET completed_on_utc = @completedOnUtc, outcome = @outcome, summary = @summary WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", runId);
            command.Parameters.AddWithValue("completedOnUtc", completedOnUtc);
            command.Parameters.AddWithValue("outcome", outcome);
            command.Parameters.AddWithValue("summary", summary);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to record job run completion {RunId}; the job continues without run history", runId);
        }
    }

    public async Task RecordRunAsync(string jobName, DateTimeOffset startedOnUtc, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                "INSERT INTO job_runs (id, job_name, started_on_utc, completed_on_utc, outcome, summary) " +
                "VALUES (@id, @jobName, @startedOnUtc, @completedOnUtc, @outcome, @summary)", connection);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("jobName", jobName);
            command.Parameters.AddWithValue("startedOnUtc", startedOnUtc);
            command.Parameters.AddWithValue("completedOnUtc", completedOnUtc);
            command.Parameters.AddWithValue("outcome", outcome);
            command.Parameters.AddWithValue("summary", summary);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await PurgeIfDueAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to record job run for {JobName}; the job continues without run history", jobName);
        }
    }

    private async Task PurgeIfDueAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // Opportunistic retention: at most one purge attempt per day per process.
        var nowUtc = DateTimeOffset.UtcNow;
        if (nowUtc - _lastPurgeUtc < TimeSpan.FromHours(24))
            return;
        _lastPurgeUtc = nowUtc;

        await using var command = new NpgsqlCommand(
            "DELETE FROM job_runs WHERE started_on_utc < @cutoffUtc", connection);
        command.Parameters.AddWithValue("cutoffUtc", nowUtc.AddDays(-_retentionDays));
        var purged = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (purged > 0)
            _logger.LogInformation("Job-run retention purge deleted {Count} rows older than {RetentionDays} days", purged, _retentionDays);
    }
}
