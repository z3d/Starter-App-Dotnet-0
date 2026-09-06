using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace StarterApp.Functions;

// Service Bus has no Functions execution-retry policy. Retry handler work explicitly while the
// message lock is held; settling a successful handler is outside the loop to avoid repeating work
// when Complete fails. The execution deadline bounds handler work and backoff only: settlement
// runs on the host token afterwards, so a poison message that surfaces late is still dead-lettered
// and a handler that finished late is still completed. A host shutdown or an expired deadline
// mid-handler leaves the message unsettled for redelivery.
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
                // Poison is poison however late it surfaced: settle on the host token so an expired
                // deadline cannot demote dead-lettering into a redelivery that burns MaxDeliveryCount.
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
