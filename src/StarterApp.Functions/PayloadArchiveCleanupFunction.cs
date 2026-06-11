using System.Globalization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Jobs;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public sealed class PayloadArchiveCleanupFunction
{
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

    // The %setting% lookup must use the ':' configuration-key form: the environment-variable
    // configuration provider normalizes the PayloadCapture__CleanupCron env var to the
    // PayloadCapture:CleanupCron key, so a literal '__' lookup resolves to null and the failed
    // timer indexing can take down the Service Bus subscribers in the same worker.
    [Function(nameof(PayloadArchiveCleanupFunction))]
    public async Task RunAsync([TimerTrigger("%PayloadCapture:CleanupCron%")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        var startedOnUtc = _timeProvider.GetUtcNow();
        var runId = await _jobRunRecorder.StartRunAsync("payload-archive-cleanup", startedOnUtc, cancellationToken);

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

            var summary = string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"archiveDeleted\":{result.ArchiveDeleted},\"auditDeleted\":{result.AuditDeleted},\"entityIndexDeleted\":{result.EntityIndexDeleted},\"totalDeleted\":{result.TotalDeleted}}}");
            await _jobRunRecorder.CompleteRunAsync(runId, _timeProvider.GetUtcNow(), "Succeeded", summary, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _jobRunRecorder.CompleteRunAsync(
                runId,
                _timeProvider.GetUtcNow(),
                "Failed",
                $"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}",
                cancellationToken);
            throw;
        }
    }
}
