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
            try
            {
                await ProcessBatchAsync(stoppingToken);

                if (_timeProvider.GetUtcNow() - _lastCleanupUtc >= TimeSpan.FromMinutes(_options.CleanupIntervalMinutes))
                {
                    _runAggregator.AddPurged(await CleanupExpiredMessagesAsync(stoppingToken));
                    _lastCleanupUtc = _timeProvider.GetUtcNow();
                }

                // One job_runs row per interval with activity, never per message.
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

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }
    }

    // Errored rows are purged RetentionDays after the failure, not the event time; unprocessed rows are never touched.
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
            // InMemory fallback (tests): ExecuteDeleteAsync is not supported on that provider.
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

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var processingId = Guid.CreateVersion7();
        var now = _timeProvider.GetUtcNow();
        var lockedUntilUtc = now.AddSeconds(_options.LockDurationSeconds);

        // The transaction covers only the claim, so row locks are released before any network call.
        var messages = await strategy.ExecuteAsync(cancellationToken, async ct =>
        {
            dbContext.ChangeTracker.Clear();
            return await ClaimBatchInTransactionAsync(dbContext, processingId, now, lockedUntilUtc, ct);
        });

        if (messages.Count == 0)
            return;

        _logger.LogInformation("Processing {Count} outbox messages with processing id {ProcessingId}", messages.Count, processingId);

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
                    // FailClosed: nothing was sent, so pause the batch and leave the retry budget alone.
                    _logger.LogWarning(captureEx,
                        "Payload capture failed for outbox message {MessageId} ({Type}) before publish; pausing batch until the archive store recovers (retry budget untouched)",
                        message.Id, message.Type);
                    _runAggregator.AddPaused();
                    break;
                }

                await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);

                message.MarkAsProcessed(_timeProvider.GetUtcNow());
                _runAggregator.AddPublished();
                _logger.LogInformation("Published outbox message {MessageId} ({Type})", message.Id, message.Type);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A broker outage must not spend each message's retry budget: pause the batch and keep the claims locked until LockedUntilUtc.
                if (IsTransientDependencyError(ex))
                {
                    _logger.LogWarning(ex, "Transient dependency error publishing outbox message {MessageId} ({Type}); pausing batch until claim lock expires",
                        message.Id, message.Type);
                    _runAggregator.AddPaused();
                    break;
                }

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
        }

        await SaveOutcomesAsync(dbContext, cancellationToken);
    }

    // A row another replica reclaimed mid-batch would fail the whole save and republish everything; detach it and save the rest.
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

    // internal so integration tests can drive SKIP LOCKED against real PostgreSQL.
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

        // InMemory fallback (tests)
        return await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null &&
                        m.Error == null &&
                        (m.LockedUntilUtc == null || m.LockedUntilUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
    }

    // For dead-letter resubmits the captured record is the only durable artifact, so it carries the replay marker too.
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

        // Lets audit and subscribers tell an operator republish from a first delivery (docs/runbooks/event-replay.md).
        if (message.ReplayCount > 0)
        {
            serviceBusMessage.ApplicationProperties["Replay"] = true;
            serviceBusMessage.ApplicationProperties["ReplayCount"] = message.ReplayCount;
        }

        return serviceBusMessage;
    }
}
