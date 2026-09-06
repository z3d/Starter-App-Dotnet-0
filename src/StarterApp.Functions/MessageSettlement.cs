using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace StarterApp.Functions;

// Service Bus has no Functions execution-retry policy. Retry handler work explicitly while the
// message lock is held; settling a successful handler is outside the loop to avoid repeating work
// when Complete fails. A host shutdown or deadline leaves the message unsettled for redelivery.
public static class MessageSettlement
{
    private const int MaxReasonDescriptionLength = 2048;
    internal const int MaxRetries = 5;
    internal static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(4);

    public static Task SettleAsync(
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        ILogger logger,
        Func<ServiceBusReceivedMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
        => SettleAsync(message, messageActions, logger, handler, Task.Delay, ExecutionTimeout, cancellationToken);

    // Inject only the wait and deadline for deterministic retry tests. Production uses real delays
    // and one total deadline shared by handler execution, backoff, and settlement.
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
                token.ThrowIfCancellationRequested();
                break;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsNonRetryable(exception))
            {
                token.ThrowIfCancellationRequested();
                logger.LogError(
                    "Dead-lettering message {MessageId} ({Subject}, correlation {CorrelationId}): {FailureType} cannot succeed on redelivery",
                    message.MessageId, message.Subject, message.CorrelationId, exception.GetType().Name);
                await messageActions.DeadLetterMessageAsync(message,
                    deadLetterReason: exception.GetType().Name,
                    deadLetterErrorDescription: DescribeFailure(exception, message), cancellationToken: token);
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
                    await messageActions.AbandonMessageAsync(message, cancellationToken: token);
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, attempt), 45));
                logger.LogWarning(
                    "Transient {FailureType} on message {MessageId} ({Subject}, correlation {CorrelationId}); retry {Retry} of {MaxRetries} after {RetryDelay}",
                    exception.GetType().Name, message.MessageId, message.Subject, message.CorrelationId,
                    attempt + 1, MaxRetries, delay);
                await delayAsync(delay, token);
            }
        }

        await messageActions.CompleteMessageAsync(message, token);
    }

    // Redelivering the same bytes cannot fix these. Extend as real handler logic lands
    // (e.g. domain rule violations surfaced while applying an event).
    public static bool IsNonRetryable(Exception exception) =>
        exception is JsonException or InvalidDataException;

    // The dead-letter description is unredacted broker metadata — and nothing in this worker is
    // redacted (see the comment on the failure branches above) — so it must carry no payload-derived
    // text: exception.Message can echo payload content once handlers deserialize domain events
    // (e.g. a JSON path or a domain-rule message quoting a field value).
    // Emit only the exception type and the correlation id — support jumps to the correlation-bound,
    // full-fidelity archive blob for the actual payload. Truncate as a guard against an unexpectedly
    // long correlation id (the broker enforces a hard ceiling on this field regardless of source).
    private static string DescribeFailure(Exception exception, ServiceBusReceivedMessage message) =>
        Truncate($"{exception.GetType().Name}; correlationId={message.CorrelationId}");

    private static string Truncate(string value) =>
        value.Length <= MaxReasonDescriptionLength ? value : value[..MaxReasonDescriptionLength];
}
