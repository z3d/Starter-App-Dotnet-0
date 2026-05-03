using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public sealed class PayloadArchiveCleanupFunction
{
    private readonly IPayloadArchiveStore _payloadArchiveStore;
    private readonly TimeProvider _timeProvider;
    private readonly PayloadCaptureOptions _options;
    private readonly ILogger<PayloadArchiveCleanupFunction> _logger;

    public PayloadArchiveCleanupFunction(
        IPayloadArchiveStore payloadArchiveStore,
        TimeProvider timeProvider,
        IOptions<PayloadCaptureOptions> options,
        ILogger<PayloadArchiveCleanupFunction> logger)
    {
        _payloadArchiveStore = payloadArchiveStore;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    [Function(nameof(PayloadArchiveCleanupFunction))]
    public async Task RunAsync([TimerTrigger("%PayloadCapture__CleanupCron%")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        var cutoffUtc = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
        var result = await _payloadArchiveStore.DeleteOlderThanAsync(cutoffUtc, cancellationToken);

        _logger.LogInformation(
            "Payload archive cleanup completed. CutoffUtc: {CutoffUtc}, ArchiveDeleted: {ArchiveDeleted}, AuditDeleted: {AuditDeleted}, EntityIndexDeleted: {EntityIndexDeleted}, TotalDeleted: {TotalDeleted}",
            cutoffUtc,
            result.ArchiveDeleted,
            result.AuditDeleted,
            result.EntityIndexDeleted,
            result.TotalDeleted);
    }
}
