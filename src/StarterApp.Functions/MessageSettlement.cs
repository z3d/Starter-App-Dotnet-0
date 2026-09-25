using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

// Complete sits outside the retry loop so a failed Complete does not rerun the handler; settlement runs on the host token, so a late finish is still completed.
public static class MessageSettlement
{
    private const int MaxReasonDescriptionLength = 2048;
    internal const int MaxRetries = 5;

    // Backoff alone is 120 s (5/10/20/40/45); MessageSettlementTests pins the remainder as per-attempt budget.
    internal static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(210);
    internal static readonly TimeSpan MinimumHandlerBudgetPerAttempt = TimeSpan.FromSeconds(15);

    // FunctionsHostConfigConventionTests keeps ExecutionTimeout + SettlementReserve inside maxAutoLockRenewalDuration.
    internal static readonly TimeSpan SettlementReserve = TimeSpan.FromSeconds(30);

    public static Task SettleAsync(
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        ILogger logger,
        Func<ServiceBusReceivedMessage, CancellationToken, Task> handler,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
        => SettleAsync(message, messageActions, logger, handler,
            (delay, token) => Task.Delay(delay, timeProvider, token), ExecutionTimeout, cancellationToken);

    internal static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, attempt), 45));

    internal static TimeSpan TotalBackoff =>
        Enumerable.Range(0, MaxRetries).Aggregate(TimeSpan.Zero, (sum, attempt) => sum + BackoffFor(attempt));

    // Wait and deadline are injected for deterministic tests.
    internal static async Task SettleAsync(
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        ILogger logger,
        Func<ServiceBusReceivedMessage, CancellationToken, Task> handler,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(executionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;

        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await handler(message, token);
                // Finished work is completed even past the deadline; only a host shutdown leaves it.
                cancellationToken.ThrowIfCancellationRequested();
                break;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsNonRetryable(exception))
            {
                // An expired deadline must not turn a dead-letter into a redelivery that spends MaxDeliveryCount.
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogError(
                    "Dead-lettering message {MessageId} ({Subject}, correlation {CorrelationId}): {FailureType} cannot succeed on redelivery",
                    message.MessageId, message.Subject, message.CorrelationId, exception.GetType().Name);
                await messageActions.DeadLetterMessageAsync(message,
                    deadLetterReason: exception.GetType().Name,
                    deadLetterErrorDescription: DescribeFailure(exception, message), cancellationToken: cancellationToken);
                return;
            }
            catch (Exception exception)
            {
                token.ThrowIfCancellationRequested();
                // No exception objects: worker logs have no PII redaction stage.
                if (attempt == MaxRetries)
                {
                    logger.LogError(
                        "Abandoning message {MessageId} ({Subject}, correlation {CorrelationId}) after handler retries ({FailureType}); broker redelivery takes over",
                        message.MessageId, message.Subject, message.CorrelationId, exception.GetType().Name);
                    await messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
                    return;
                }

                var delay = BackoffFor(attempt);
                logger.LogWarning(
                    "Transient {FailureType} on message {MessageId} ({Subject}, correlation {CorrelationId}); retry {Retry} of {MaxRetries} after {RetryDelay}",
                    exception.GetType().Name, message.MessageId, message.Subject, message.CorrelationId,
                    attempt + 1, MaxRetries, delay);
                await delayAsync(delay, token);
            }
        }

        await messageActions.CompleteMessageAsync(message, cancellationToken);
    }

    // Redelivering the same bytes cannot fix these.
    public static bool IsNonRetryable(Exception exception) =>
        exception is JsonException or InvalidDataException;

    // Broker metadata is never redacted and an exception message can quote the payload, so only the type and correlation id go here.
    private static string DescribeFailure(Exception exception, ServiceBusReceivedMessage message) =>
        Truncate($"{exception.GetType().Name}; correlationId={message.CorrelationId}");

    private static string Truncate(string value) =>
        value.Length <= MaxReasonDescriptionLength ? value : value[..MaxReasonDescriptionLength];

    internal static string ResolveCorrelationId(ServiceBusReceivedMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
            return CorrelationContext.Sanitize(message.CorrelationId);

        if (message.ApplicationProperties.TryGetValue(CorrelationContext.ApplicationPropertyName, out var value) && value is string correlationId)
            return CorrelationContext.Sanitize(correlationId);

        return CorrelationContext.Create();
    }

    // For dead-letter resubmits this captured record is the only durable artifact of the redelivery.
    internal static Dictionary<string, string> BuildCaptureMetadata(ServiceBusReceivedMessage message, string subscription)
    {
        var metadata = new Dictionary<string, string>
        {
            ["messageId"] = message.MessageId,
            ["subject"] = message.Subject ?? string.Empty,
            ["subscription"] = subscription,
            ["topic"] = "domain-events"
        };

        if (message.ApplicationProperties.TryGetValue("Replay", out var replay) && replay is true)
        {
            metadata["replay"] = "true";
            if (message.ApplicationProperties.TryGetValue("ReplayCount", out var count))
                metadata["replayCount"] = count?.ToString() ?? "1";
        }

        return metadata;
    }
}
