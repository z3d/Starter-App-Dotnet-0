using Microsoft.Extensions.Logging;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class JobRunRecorderTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public JobRunRecorderTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public async Task DisposeAsync() => await Task.CompletedTask;

    private NpgsqlJobRunRecorder CreateRecorder(int retentionDays = 30) =>
        new(_fixture.ConnectionString, retentionDays, new LoggerFactory().CreateLogger<NpgsqlJobRunRecorder>());

    [Fact]
    public async Task StartAndComplete_PersistsTheFullRunRecord()
    {
        var recorder = CreateRecorder();
        var startedOnUtc = DateTimeOffset.UtcNow;

        var runId = await recorder.StartRunAsync("test-job", startedOnUtc, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, runId);

        await recorder.CompleteRunAsync(runId, startedOnUtc.AddSeconds(5), "Succeeded", "{\"deleted\":3}", CancellationToken.None);

        var row = await LoadRunAsync(runId);
        Assert.Equal("test-job", row.JobName);
        Assert.Equal("Succeeded", row.Outcome);
        Assert.Equal("{\"deleted\":3}", row.Summary);
        Assert.NotNull(row.CompletedOnUtc);
    }

    [Fact]
    public async Task RecordRun_PersistsASingleShotAggregateRow()
    {
        var recorder = CreateRecorder();
        var startedOnUtc = DateTimeOffset.UtcNow.AddMinutes(-15);

        await recorder.RecordRunAsync("outbox-processor", startedOnUtc, DateTimeOffset.UtcNow, "Degraded",
            "{\"published\":10,\"errored\":1,\"retried\":2,\"purged\":0}", CancellationToken.None);

        var (count, outcome) = await CountRunsAsync("outbox-processor");
        Assert.Equal(1, count);
        Assert.Equal("Degraded", outcome);
    }

    [Fact]
    public async Task RecorderFailure_IsSwallowed_AndNeverThrows()
    {
        var broken = new NpgsqlJobRunRecorder(
            "Host=localhost;Port=1;Database=nope;Username=x;Password=y;Timeout=1",
            30,
            new LoggerFactory().CreateLogger<NpgsqlJobRunRecorder>());

        var runId = await broken.StartRunAsync("test-job", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(Guid.Empty, runId);
        await broken.CompleteRunAsync(runId, DateTimeOffset.UtcNow, "Succeeded", "{}", CancellationToken.None);
        await broken.RecordRunAsync("test-job", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Succeeded", "{}", CancellationToken.None);
    }

    [Fact]
    public async Task RetentionPurge_DeletesRowsOlderThanRetention()
    {
        var recorder = CreateRecorder(retentionDays: 7);

        // An old row well past retention, inserted directly.
        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO job_runs (id, job_name, started_on_utc, completed_on_utc, outcome, summary) " +
                "VALUES (@id, 'old-job', now() - interval '30 days', now() - interval '30 days', 'Succeeded', '{}')", connection);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            await command.ExecuteNonQueryAsync();
        }

        // Any recording call triggers the opportunistic purge (first call in this process window).
        await recorder.RecordRunAsync("fresh-job", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Succeeded", "{}", CancellationToken.None);

        var (oldCount, _) = await CountRunsAsync("old-job");
        var (freshCount, _) = await CountRunsAsync("fresh-job");
        Assert.Equal(0, oldCount);
        Assert.Equal(1, freshCount);
    }

    private async Task<(string JobName, string? Outcome, string? Summary, DateTimeOffset? CompletedOnUtc)> LoadRunAsync(Guid runId)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT job_name, outcome, summary, completed_on_utc FROM job_runs WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", runId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"job_runs row {runId} not found");
        return (
            reader.GetString(0),
            await reader.IsDBNullAsync(1) ? null : reader.GetString(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
            await reader.IsDBNullAsync(3) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(3));
    }

    private async Task<(long Count, string? Outcome)> CountRunsAsync(string jobName)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*), max(outcome) FROM job_runs WHERE job_name = @jobName", connection);
        command.Parameters.AddWithValue("jobName", jobName);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), await reader.IsDBNullAsync(1) ? null : reader.GetString(1));
    }
}
