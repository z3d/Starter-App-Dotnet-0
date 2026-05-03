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

        // EnableRetryOnFailure's execution strategy rejects user-initiated transactions unless
        // wrapped in ExecuteAsync. We need a user transaction here for row-level locking
        // (UPDLOCK, READPAST, ROWLOCK) so multiple replicas don't publish the same rows.
        // ChangeTracker.Clear at the top of each attempt guarantees a clean state on retry.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(cancellationToken, async ct =>
        {
            dbContext.ChangeTracker.Clear();
            await ProcessBatchInTransactionAsync(dbContext, ct);
        });
    }

    private async Task ProcessBatchInTransactionAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var messages = await ClaimUnprocessedMessagesAsync(dbContext, cancellationToken);

            if (messages.Count == 0)
            {
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);
                return;
            }

            _logger.LogInformation("Processing {Count} outbox messages", messages.Count);

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
                    // Dependency-level outages (SB down, throttled, timing out) should not consume
                    // a message's per-message retry budget — otherwise a multi-minute outage poisons
                    // every polled message into a permanent Error state requiring manual requeue.
                    // Break the batch; the next poll tick re-attempts the same messages cleanly.
                    if (IsTransientServiceBusError(ex))
                    {
                        _logger.LogWarning(ex, "Transient Service Bus error publishing outbox message {MessageId} ({Type}); pausing batch, will retry on next poll",
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

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
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

    private async Task<List<OutboxMessage>> ClaimUnprocessedMessagesAsync(
        ApplicationDbContext dbContext,
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
                    WHERE ProcessedOnUtc IS NULL AND Error IS NULL
                    ORDER BY OccurredOnUtc
                    """,
                    _options.BatchSize)
                .ToListAsync(cancellationToken);
        }

        // InMemory fallback (tests)
        return await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null && m.Error == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
    }
}
