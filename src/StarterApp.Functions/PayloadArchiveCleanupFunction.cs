using System.Globalization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Jobs;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public sealed class PayloadArchiveCleanupFunction
{
    public const string JobName = "payload-archive-cleanup";

    private readonly IPayloadArchiveStore _payloadArchiveStore;
    private readonly TimeProvider _timeProvider;
    private readonly PayloadCaptureOptions _options;
    private readonly IJobRunRecorder _jobRunRecorder;
    private readonly ILogger<PayloadArchiveCleanupFunction> _logger;

    public PayloadArchiveCleanupFunction(
        IPayloadArchiveStore payloadArchiveStore,
        TimeProvider timeProvider,
        IOptions<PayloadCaptureOptions> options,
        IJobRunRecorder jobRunRecorder,
        ILogger<PayloadArchiveCleanupFunction> logger)
    {
        _payloadArchiveStore = payloadArchiveStore;
        _timeProvider = timeProvider;
        _options = options.Value;
        _jobRunRecorder = jobRunRecorder;
        _logger = logger;
    }

    // %setting% must use the ':' key form; a '__' lookup resolves to null and the failed timer takes down the subscribers in this worker.
    [Function(nameof(PayloadArchiveCleanupFunction))]
    [WatchedJob(JobName)]
    public async Task RunAsync([TimerTrigger("%PayloadCapture:CleanupCron%")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        var startedOnUtc = _timeProvider.GetUtcNow();
        var runId = await _jobRunRecorder.StartRunAsync(JobName, startedOnUtc, cancellationToken);

        try
        {
            var cutoffUtc = startedOnUtc.AddDays(-_options.RetentionDays);
            var result = await _payloadArchiveStore.DeleteOlderThanAsync(cutoffUtc, cancellationToken);

            _logger.LogInformation(
                "Payload archive cleanup completed. CutoffUtc: {CutoffUtc}, ArchiveDeleted: {ArchiveDeleted}, AuditDeleted: {AuditDeleted}, EntityIndexDeleted: {EntityIndexDeleted}, TotalDeleted: {TotalDeleted}",
                cutoffUtc,
                result.ArchiveDeleted,
                result.AuditDeleted,
                result.EntityIndexDeleted,
                result.TotalDeleted);

            if (result.BudgetExhausted)
            {
                _logger.LogWarning(
                    "Payload archive cleanup stopped at its time budget ({BudgetSeconds}s) before catching up; expired payloads remain past RetentionDays. Raise CleanupTimeBudgetSeconds (with functionTimeout) or the schedule frequency.",
                    _options.CleanupTimeBudgetSeconds);
            }

            var summary = string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"archiveDeleted\":{result.ArchiveDeleted},\"auditDeleted\":{result.AuditDeleted},\"entityIndexDeleted\":{result.EntityIndexDeleted},\"totalDeleted\":{result.TotalDeleted},\"budgetExhausted\":{(result.BudgetExhausted ? "true" : "false")}}}");
            var outcome = result.BudgetExhausted ? JobOutcomes.Degraded : JobOutcomes.Succeeded;
            await _jobRunRecorder.CompleteRunAsync(runId, _timeProvider.GetUtcNow(), outcome, summary, cancellationToken);
            JobRunMetrics.Record(JobName, outcome);
        }
        catch (OperationCanceledException)
        {
            // The run's own token is the cancelled one, so the outcome is written on a fresh, short one.
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
