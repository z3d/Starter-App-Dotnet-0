using System.Globalization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Jobs;

namespace StarterApp.Functions;

public sealed class IdempotencyRecordCleanupFunction
{
    public const string JobName = "idempotency-record-cleanup";

    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(4);

    private readonly IdempotencyRecordPurge _purge;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly IJobRunRecorder _jobRunRecorder;
    private readonly ILogger<IdempotencyRecordCleanupFunction> _logger;

    public IdempotencyRecordCleanupFunction(
        IdempotencyRecordPurge purge,
        TimeProvider timeProvider,
        IConfiguration configuration,
        IJobRunRecorder jobRunRecorder,
        ILogger<IdempotencyRecordCleanupFunction> logger)
    {
        _purge = purge;
        _timeProvider = timeProvider;
        _configuration = configuration;
        _jobRunRecorder = jobRunRecorder;
        _logger = logger;
    }

    // The %setting% key must use ':' not '__'. With '__' the lookup returns null, the timer fails to start, and it takes every subscriber in this worker down with it.
    [Function(nameof(IdempotencyRecordCleanupFunction))]
    [WatchedJob(JobName)]
    public async Task RunAsync([TimerTrigger("%Idempotency:CleanupCron%")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        var startedOnUtc = _timeProvider.GetUtcNow();
        var runId = await _jobRunRecorder.StartRunAsync(JobName, startedOnUtc, cancellationToken);

        try
        {
            var cutoffUtc = startedOnUtc.AddDays(-_configuration.GetValue("Idempotency:RetentionDays", 30));
            var result = await _purge.DeleteOlderThanAsync(cutoffUtc, Budget, _timeProvider, cancellationToken);

            _logger.LogInformation("Idempotency record cleanup deleted {Deleted} records created before {CutoffUtc}", result.Deleted, cutoffUtc);
            if (result.BudgetExhausted)
                _logger.LogWarning("Idempotency record cleanup stopped at its time budget before catching up; the rest go next run.");

            var summary = string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"deleted\":{result.Deleted},\"budgetExhausted\":{(result.BudgetExhausted ? "true" : "false")}}}");
            var outcome = result.BudgetExhausted ? JobOutcomes.Degraded : JobOutcomes.Succeeded;
            await _jobRunRecorder.CompleteRunAsync(runId, _timeProvider.GetUtcNow(), outcome, summary, cancellationToken);
            JobRunMetrics.Record(JobName, outcome);
        }
        catch (OperationCanceledException)
        {
            using var recording = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _jobRunRecorder.CompleteRunAsync(runId, _timeProvider.GetUtcNow(), JobOutcomes.Cancelled, "{\"error\":\"The run was cancelled before it finished\"}", recording.Token);
            JobRunMetrics.Record(JobName, JobOutcomes.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            await _jobRunRecorder.CompleteRunAsync(
                runId,
                _timeProvider.GetUtcNow(),
                JobOutcomes.Failed,
                $"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}",
                cancellationToken);
            JobRunMetrics.Record(JobName, JobOutcomes.Failed);
            throw;
        }
    }
}
