using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace StarterApp.Functions;

// Manual settlement for the Service Bus subscribers (host.json sets autoCompleteMessages: false).
// Outcomes:
//   handler succeeds              -> Complete.
//   non-retryable failure         -> DeadLetter, with the exception type as the reason — redelivery
//                                    cannot fix a payload that does not parse. The description carries
//                                    only the type + correlation id (never payload-derived text; see
//                                    DescribeFailure), so this broker metadata stays PII-free.
//   transient, retries remaining  -> rethrow so the host retry policy re-runs in-process while the
//                                    lock is held; FunctionsHostConfigConventionTests pins the
//                                    worst-case retry window inside maxAutoLockRenewalDuration.
//   transient, retries exhausted  -> Abandon for prompt redelivery instead of waiting out the lock;
//                                    the subscription's MaxDeliveryCount is the poison backstop.
//   host shutdown (cancellation)  -> leave unsettled; the lock lapses and the message redelivers.
public static class MessageSettlement
{
    // Dead-letter descriptions have a broker size ceiling; keep well under it.
    private const int MaxReasonDescriptionLength = 2048;

    // The handler is a method group (message, token) => Task rather than a closure: the async-suffix
    // and payload-capture conventions scan the function types' declared and one-level-nested methods,
    // and a Task-returning lambda lands in a display class that fails the former and hides from the
    // latter. A named private ProcessAsync on the function class satisfies both.
    public static async Task SettleAsync(
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        RetryContext? retryContext,
        ILogger logger,
        Func<ServiceBusReceivedMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler(message, cancellationToken);
            await messageActions.CompleteMessageAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNonRetryable(exception))
        {
            logger.LogError(exception,
                "Dead-lettering message {MessageId} ({Subject}): {FailureType} cannot succeed on redelivery",
                message.MessageId, message.Subject, exception.GetType().Name);

            await messageActions.DeadLetterMessageAsync(
                message,
                deadLetterReason: exception.GetType().Name,
                deadLetterErrorDescription: DescribeFailure(exception, message),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (HasRetriesRemaining(retryContext))
        {
            logger.LogWarning(exception,
                "Transient failure on message {MessageId} ({Subject}); attempt {Attempt} of {TotalAttempts}, host retry re-runs in-process",
                message.MessageId, message.Subject,
                retryContext!.RetryCount + 1, retryContext.MaxRetryCount + 1);

            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Abandoning message {MessageId} ({Subject}) after in-process retries; broker redelivery takes over",
                message.MessageId, message.Subject);

            await messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
        }
    }

    // Redelivering the same bytes cannot fix these. Extend as real handler logic lands
    // (e.g. domain rule violations surfaced while applying an event).
    public static bool IsNonRetryable(Exception exception) =>
        exception is JsonException or InvalidDataException;

    private static bool HasRetriesRemaining(RetryContext? retryContext) =>
        retryContext is not null && retryContext.RetryCount < retryContext.MaxRetryCount;

    // The dead-letter description is unredacted broker metadata (no Serilog masking reaches it), so it
    // must carry no payload-derived text: exception.Message can echo payload content once handlers
    // deserialize domain events (e.g. a JSON path or a domain-rule message quoting a field value).
    // Emit only the exception type and the correlation id — support jumps to the correlation-bound,
    // full-fidelity archive blob for the actual payload. Truncate as a guard against an unexpectedly
    // long correlation id (the broker enforces a hard ceiling on this field regardless of source).
    private static string DescribeFailure(Exception exception, ServiceBusReceivedMessage message) =>
        Truncate($"{exception.GetType().Name}; correlationId={message.CorrelationId}");

    private static string Truncate(string value) =>
        value.Length <= MaxReasonDescriptionLength ? value : value[..MaxReasonDescriptionLength];
}
