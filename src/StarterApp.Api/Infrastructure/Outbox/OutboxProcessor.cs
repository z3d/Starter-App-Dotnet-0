using Azure;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Jobs;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.Outbox;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusSender _sender;
    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly OutboxProcessorOptions _options;
    private readonly IJobRunRecorder _jobRunRecorder;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly OutboxRunAggregator _runAggregator;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _lastCleanupUtc;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ServiceBusSender sender,
        IPayloadCaptureSink payloadCaptureSink,
        IOptions<OutboxProcessorOptions> options,
        IJobRunRecorder jobRunRecorder,
        ILogger<OutboxProcessor> logger,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _scopeFactory = scopeFactory;
        _sender = sender;
        _payloadCaptureSink = payloadCaptureSink;
        _options = options.Value;
        _jobRunRecorder = jobRunRecorder;
        _logger = logger;
        _runAggregator = new OutboxRunAggregator(TimeSpan.FromMinutes(_options.HealthRowIntervalMinutes), _timeProvider.GetUtcNow());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessor started. Polling every {Interval}s, batch size {BatchSize}",
            _options.PollingIntervalSeconds, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            var backlogRemains = false;
            try
            {
                backlogRemains = await ProcessBatchAsync(stoppingToken);

                if (_timeProvider.GetUtcNow() - _lastCleanupUtc >= TimeSpan.FromMinutes(_options.CleanupIntervalMinutes))
                {
                    _runAggregator.AddPurged(await CleanupExpiredMessagesAsync(stoppingToken));
                    _lastCleanupUtc = _timeProvider.GetUtcNow();
                }

                // One job_runs row for each interval that did something, never one per message.
                var healthWindow = _runAggregator.TryFlush(_timeProvider.GetUtcNow());
                if (healthWindow is not null)
                {
                    await _jobRunRecorder.RecordRunAsync(
                        "outbox-processor",
                        healthWindow.StartedOnUtc,
                        healthWindow.CompletedOnUtc,
                        healthWindow.Outcome,
                        healthWindow.ToSummaryJson(),
                        stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in outbox processing loop");
            }

            if (!backlogRemains)
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }
    }

    // An errored row is purged RetentionDays after it failed, not after the event happened. Unprocessed rows are never purged.
    internal async Task<int> CleanupExpiredMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoffUtc = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);

        int deleted;
        if (dbContext.Database.IsRelational())
        {
            deleted = await dbContext.OutboxMessages
                .Where(m => (m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoffUtc) ||
                            (m.ErroredOnUtc != null && m.ErroredOnUtc < cutoffUtc))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            // The in-memory test provider does not support ExecuteDeleteAsync, so tests take this path.
            var expired = await dbContext.OutboxMessages
                .Where(m => (m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoffUtc) ||
                            (m.ErroredOnUtc != null && m.ErroredOnUtc < cutoffUtc))
                .ToListAsync(cancellationToken);
            dbContext.OutboxMessages.RemoveRange(expired);
            await dbContext.SaveChangesAsync(cancellationToken);
            deleted = expired.Count;
        }

        if (deleted > 0)
            _logger.LogInformation("Outbox retention cleanup deleted {Count} messages older than {CutoffUtc}", deleted, cutoffUtc);

        return deleted;
    }

    // Returns true only when a full batch published with no errors. A backlog then drains without sleeping, while retries and pauses still wait.
    private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var processingId = Guid.CreateVersion7();
        var now = _timeProvider.GetUtcNow();
        var lockedUntilUtc = now.AddSeconds(_options.LockDurationSeconds);

        // The transaction covers only the claim, so the row locks are released before any network call.
        var messages = await strategy.ExecuteAsync(cancellationToken, async ct =>
        {
            dbContext.ChangeTracker.Clear();
            return await ClaimBatchInTransactionAsync(dbContext, processingId, now, lockedUntilUtc, ct);
        });

        if (messages.Count == 0)
            return false;

        _logger.LogInformation("Processing {Count} outbox messages with processing id {ProcessingId}", messages.Count, processingId);

        var published = 0;

        foreach (var message in messages)
        {
            try
            {
                var serviceBusMessage = BuildServiceBusMessage(message);

                try
                {
                    await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
                    {
                        CorrelationId = message.CorrelationId,
                        Direction = "outbound",
                        Channel = PayloadCaptureChannels.ServiceBus,
                        Operation = message.Type,
                        ContentType = "application/json",
                        Payload = message.Payload,
                        Metadata = BuildCaptureMetadata(message, _options.TopicName)
                    }, cancellationToken);
                }
                catch (Exception captureEx) when (captureEx is not OperationCanceledException)
                {
                    // If the archive store is down, the batch pauses and no message loses a retry (#79). If only this message fails to capture, it loses one, so it ends up in Error where it can be replayed.
                    if (IsStoreWideCaptureFailure(captureEx))
                    {
                        _runAggregator.AddPaused();
                        if (PayloadCaptureFailureClassifier.IsTransientDependencyFailure(captureEx))
                            _logger.LogWarning(captureEx,
                                "Payload capture failed for outbox message {MessageId} ({Type}) before publish; pausing batch until the archive store recovers (retry budget untouched)",
                                message.Id, message.Type);
                        else
                            _logger.LogError(captureEx,
                                "The archive store refused capture for outbox message {MessageId} ({Type}); pausing all publishing until its configuration or permissions are fixed (retry budget untouched)",
                                message.Id, message.Type);
                        break;
                    }

                    _logger.LogError(captureEx,
                        "Payload capture failed for outbox message {MessageId} ({Type}) alone; it is not sent, and the rest of the batch publishes",
                        message.Id, message.Type);
                    SpendRetry(message, captureEx);
                    continue;
                }

                await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);

                message.MarkAsProcessed(_timeProvider.GetUtcNow());
                _runAggregator.AddPublished();
                published++;
                _logger.LogInformation("Published outbox message {MessageId} ({Type})", message.Id, message.Type);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A Service Bus outage must not cost every message a retry. The batch pauses and the claims stay locked until LockedUntilUtc.
                if (IsTransientDependencyError(ex))
                {
                    _logger.LogWarning(ex, "Transient dependency error publishing outbox message {MessageId} ({Type}); pausing batch until claim lock expires",
                        message.Id, message.Type);
                    _runAggregator.AddPaused();
                    break;
                }

                SpendRetry(message, ex);
            }
        }

        await SaveOutcomesAsync(dbContext, cancellationToken);
        return published == _options.BatchSize;
    }

    private void SpendRetry(OutboxMessage message, Exception ex)
    {
        message.IncrementRetry();

        if (message.RetryCount >= _options.MaxRetries)
        {
            message.MarkAsError(ex.Message, _timeProvider.GetUtcNow());
            _runAggregator.AddErrored();
            _logger.LogError(ex, "Outbox message {MessageId} ({Type}) permanently failed after {RetryCount} attempts",
                message.Id, message.Type, message.RetryCount);
        }
        else
        {
            _runAggregator.AddRetried();
            _logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed, will retry (attempt {RetryCount}/{MaxRetries})",
                message.Id, message.Type, message.RetryCount, _options.MaxRetries);
        }
    }

    // If another replica took over a row mid-batch, saving it would fail the whole save and republish every message. Detach that row and save the rest.
    internal async Task SaveOutcomesAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                foreach (var entry in ex.Entries)
                {
                    if (entry.Entity is OutboxMessage stolen)
                        _logger.LogWarning(
                            "Outbox message {MessageId} ({Type}) was reclaimed by another processor before its outcome could be saved; persisting the remaining batch outcomes",
                            stolen.Id, stolen.Type);

                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    // Internal so the integration tests can run SKIP LOCKED against a real PostgreSQL.
    internal async Task<List<OutboxMessage>> ClaimBatchInTransactionAsync(
        ApplicationDbContext dbContext,
        Guid processingId,
        DateTimeOffset now,
        DateTimeOffset lockedUntilUtc,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var messages = await ClaimUnprocessedMessagesAsync(dbContext, now, cancellationToken);
            foreach (var message in messages)
                message.Claim(processingId, lockedUntilUtc);

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);

            return messages;
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    private static bool IsTransientServiceBusError(Exception ex)
    {
        return ex is ServiceBusException sbEx && sbEx.Reason is
            ServiceBusFailureReason.ServiceCommunicationProblem or
            ServiceBusFailureReason.ServiceTimeout or
            ServiceBusFailureReason.ServiceBusy or
            ServiceBusFailureReason.QuotaExceeded;
    }

    // A storage error would fail every message the same way. Any other error belongs to this one message.
    internal static bool IsStoreWideCaptureFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is RequestFailedException)
                return true;
        }

        return PayloadCaptureFailureClassifier.IsTransientDependencyFailure(ex);
    }

    private static bool IsTransientDependencyError(Exception ex)
    {
        return IsTransientServiceBusError(ex) || PayloadCaptureFailureClassifier.IsTransientDependencyFailure(ex);
    }

    internal async Task<List<OutboxMessage>> ClaimUnprocessedMessagesAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.OutboxMessages
                .FromSqlRaw(
                    """
                    SELECT *
                    FROM outbox_messages
                    WHERE processed_on_utc IS NULL
                      AND error IS NULL
                      AND (locked_until_utc IS NULL OR locked_until_utc <= {1})
                    ORDER BY occurred_on_utc
                    LIMIT {0}
                    FOR UPDATE SKIP LOCKED
                    """,
                    _options.BatchSize,
                    now)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null &&
                        m.Error == null &&
                        (m.LockedUntilUtc == null || m.LockedUntilUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
    }

    // When a dead-lettered message is resubmitted, the captured record is the only lasting trace, so it carries the replay marker too.
    internal static Dictionary<string, string> BuildCaptureMetadata(OutboxMessage message, string topicName)
    {
        var metadata = new Dictionary<string, string>
        {
            ["messageId"] = message.Id.ToString(),
            ["subject"] = message.Type,
            ["topic"] = topicName
        };

        if (message.ReplayCount > 0)
        {
            metadata["replay"] = "true";
            metadata["replayCount"] = message.ReplayCount.ToString(CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    public static ServiceBusMessage BuildServiceBusMessage(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var serviceBusMessage = new ServiceBusMessage(message.Payload)
        {
            ContentType = "application/json",
            MessageId = message.Id.ToString(),
            CorrelationId = message.CorrelationId,
            Subject = message.Type
        };
        serviceBusMessage.ApplicationProperties["EventType"] = message.Type;
        serviceBusMessage.ApplicationProperties[CorrelationContext.ApplicationPropertyName] = message.CorrelationId;

        // Lets the audit trail and subscribers tell an operator's replay from a first delivery (docs/runbooks/event-replay.md).
        if (message.ReplayCount > 0)
        {
            serviceBusMessage.ApplicationProperties["Replay"] = true;
            serviceBusMessage.ApplicationProperties["ReplayCount"] = message.ReplayCount;
        }

        return serviceBusMessage;
    }
}
