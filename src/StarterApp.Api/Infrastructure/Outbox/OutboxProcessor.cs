using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.Outbox;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusSender _sender;
    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly OutboxProcessorOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ServiceBusSender sender,
        IPayloadCaptureSink payloadCaptureSink,
        IOptions<OutboxProcessorOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _sender = sender;
        _payloadCaptureSink = payloadCaptureSink;
        _options = options.Value;
        _logger = logger;
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in outbox processing loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var processingId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var lockedUntilUtc = now.AddSeconds(_options.LockDurationSeconds);

        // EnableRetryOnFailure's execution strategy rejects user-initiated transactions unless
        // wrapped in ExecuteAsync. Keep the transaction scoped only to row claiming so SQL locks
        // are released before Blob capture or Service Bus publish network calls begin.
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
                var serviceBusMessage = new ServiceBusMessage(message.Payload)
                {
                    ContentType = "application/json",
                    MessageId = message.Id.ToString(),
                    CorrelationId = message.CorrelationId,
                    Subject = message.Type
                };
                serviceBusMessage.ApplicationProperties["EventType"] = message.Type;
                serviceBusMessage.ApplicationProperties[CorrelationContext.ApplicationPropertyName] = message.CorrelationId;

                await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
                {
                    CorrelationId = message.CorrelationId,
                    Direction = "outbound",
                    Channel = "servicebus",
                    Operation = message.Type,
                    ContentType = "application/json",
                    Payload = message.Payload,
                    Metadata = new Dictionary<string, string>
                    {
                        ["messageId"] = message.Id.ToString(),
                        ["subject"] = message.Type,
                        ["topic"] = _options.TopicName
                    }
                }, cancellationToken);

                await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);

                message.MarkAsProcessed(DateTimeOffset.UtcNow);
                _logger.LogInformation("Published outbox message {MessageId} ({Type})", message.Id, message.Type);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Dependency-level outages (SB/archive down, throttled, timing out) should not consume
                // a message's per-message retry budget — otherwise a multi-minute outage poisons
                // every polled message into a permanent Error state requiring manual requeue.
                // Keep claimed rows locked until LockedUntilUtc; that gives other processors a
                // bounded retry delay and avoids repeatedly hammering the failing dependency.
                if (IsTransientDependencyError(ex))
                {
                    _logger.LogWarning(ex, "Transient dependency error publishing outbox message {MessageId} ({Type}); pausing batch until claim lock expires",
                        message.Id, message.Type);
                    break;
                }

                message.IncrementRetry();

                if (message.RetryCount >= _options.MaxRetries)
                {
                    message.MarkAsError(ex.Message);
                    _logger.LogError(ex, "Outbox message {MessageId} ({Type}) permanently failed after {RetryCount} attempts",
                        message.Id, message.Type, message.RetryCount);
                }
                else
                {
                    _logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed, will retry (attempt {RetryCount}/{MaxRetries})",
                        message.Id, message.Type, message.RetryCount, _options.MaxRetries);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<OutboxMessage>> ClaimBatchInTransactionAsync(
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

    private async Task<List<OutboxMessage>> ClaimUnprocessedMessagesAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // On SQL Server: UPDLOCK prevents concurrent processors from reading the same rows,
        // READPAST skips rows already locked by another processor, ROWLOCK minimizes lock scope.
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.OutboxMessages
                .FromSqlRaw(
                    """
                    SELECT TOP ({0}) *
                    FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ProcessedOnUtc IS NULL
                      AND Error IS NULL
                      AND (LockedUntilUtc IS NULL OR LockedUntilUtc <= {1})
                    ORDER BY OccurredOnUtc
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
}
