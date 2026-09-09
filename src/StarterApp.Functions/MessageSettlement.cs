using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

// Retry handler work while the message lock is held. Keep completion outside the retry
// loop so a failed Complete call cannot rerun the handler here.
// The deadline covers handler work and backoff; settlement uses the host token so late
// successes and permanent failures can still settle. Cancellation during handler work
// leaves the message unsettled for redelivery.
public static class MessageSettlement
{
    private const int MaxReasonDescriptionLength = 2048;
    internal const int MaxRetries = 5;

    // Handler attempts plus backoff must finish inside this window. Backoff alone is 120 seconds
    // (5/10/20/40/45); MessageSettlementTests pins what remains as per-attempt handler budget so a
    // change to MaxRetries or the schedule cannot silently make the abandon branch unreachable.
    internal static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(210);
    internal static readonly TimeSpan MinimumHandlerBudgetPerAttempt = TimeSpan.FromSeconds(15);

    // Settlement calls run after the deadline on the host token. FunctionsHostConfigConventionTests
    // keeps ExecutionTimeout + SettlementReserve inside maxAutoLockRenewalDuration with margin.
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

    // Inject only the wait and deadline for deterministic retry tests. Production waits through the
    // host's TimeProvider and uses one deadline shared by handler execution and backoff.
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
                // Use the host token so an expired handler deadline cannot prevent dead-lettering.
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
                // No exception objects: worker logs have no PII redaction stage. Host invocation
                // logging of propagated settlement/cancellation failures is outside this helper.
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

    // Redelivering the same bytes cannot fix these. Extend as real handler logic lands
    // (e.g. domain rule violations surfaced while applying an event).
    public static bool IsNonRetryable(Exception exception) =>
        exception is JsonException or InvalidDataException;

    // Dead-letter metadata is not redacted, and exception messages may contain payload data.
    // Record only the exception type and correlation ID; use the ID to find the archived payload.
    // Truncate to the broker's description limit.
    private static string DescribeFailure(Exception exception, ServiceBusReceivedMessage message) =>
        Truncate($"{exception.GetType().Name}; correlationId={message.CorrelationId}");

    private static string Truncate(string value) =>
        value.Length <= MaxReasonDescriptionLength ? value : value[..MaxReasonDescriptionLength];

    // Message correlation id, then the CorrelationId application property, else a fresh id. Shared
    // by every subscriber so a new Function copies one attribute and one log line, not sixty lines.
    internal static string ResolveCorrelationId(ServiceBusReceivedMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
            return CorrelationContext.Sanitize(message.CorrelationId);

        if (message.ApplicationProperties.TryGetValue(CorrelationContext.ApplicationPropertyName, out var value) && value is string correlationId)
            return CorrelationContext.Sanitize(correlationId);

        return CorrelationContext.Create();
    }

    // Replayed/resubmitted messages keep their marker in the inbound capture: for dead-letter
    // resubmits this captured record is the only durable artifact of the redelivery.
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
