using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StarterApp.Functions;

namespace StarterApp.Tests.Functions;

public class MessageSettlementTests
{
    [Fact]
    public async Task SettleAsync_LogsNeverCarryTheExceptionObjectOrItsMessageText()
    {
        // The Functions worker's logs flow to OpenTelemetry with no redaction stage, so settlement
        // must never attach the exception object (whose Message can echo payload text once handlers
        // deserialize domain events) to a log entry. Drive every failure branch with a PII sentinel
        // in the exception message and assert it reaches neither the log output nor the log event.
        const string sentinel = "payload contains user@example.com";
        var logger = new RecordingLogger();

        // Non-retryable -> dead-letter branch.
        await MessageSettlement.SettleAsync(NewMessage(), new RecordingMessageActions(), retryContext: NewRetryContext(0, 5),
            logger, (_, _) => throw new JsonException(sentinel), CancellationToken.None);

        // Transient with retries remaining -> rethrow branch.
        await Assert.ThrowsAsync<TimeoutException>(() =>
            MessageSettlement.SettleAsync(NewMessage(), new RecordingMessageActions(), retryContext: NewRetryContext(0, 5),
                logger, (_, _) => throw new TimeoutException(sentinel), CancellationToken.None));

        // Transient exhausted -> abandon branch.
        await MessageSettlement.SettleAsync(NewMessage(), new RecordingMessageActions(), retryContext: NewRetryContext(5, 5),
            logger, (_, _) => throw new TimeoutException(sentinel), CancellationToken.None);

        Assert.Equal(3, logger.Entries.Count);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(sentinel, entry.RenderedMessage);
        });
    }

    [Fact]
    public async Task SettleAsync_WhenHandlerSucceeds_CompletesTheMessage()
    {
        var actions = new RecordingMessageActions();

        await MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: null,
            NullLogger.Instance, (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(new[] { "complete" }, actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenFailureIsNonRetryable_DeadLettersWithTypeAndCorrelationOnly()
    {
        var actions = new RecordingMessageActions();

        await MessageSettlement.SettleAsync(NewMessage(correlationId: "corr-42"), actions, retryContext: NewRetryContext(0, 5),
            NullLogger.Instance, (_, _) => throw new JsonException("payload contains user@example.com"), CancellationToken.None);

        Assert.Equal(new[] { "deadletter" }, actions.Calls);
        Assert.Equal(nameof(JsonException), actions.DeadLetterReason);
        // The dead-letter description is unredacted broker metadata: it must expose the exception type
        // and correlation id (to reach the archive) but never the payload-derived exception message.
        Assert.Contains(nameof(JsonException), actions.DeadLetterDescription);
        Assert.Contains("corr-42", actions.DeadLetterDescription);
        Assert.DoesNotContain("user@example.com", actions.DeadLetterDescription);
    }

    [Fact]
    public async Task SettleAsync_WhenTransientWithRetriesRemaining_RethrowsWithoutSettling()
    {
        var actions = new RecordingMessageActions();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: NewRetryContext(0, 5),
                NullLogger.Instance, (_, _) => throw new TimeoutException(), CancellationToken.None));

        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenTransientOnFinalAttempt_AbandonsTheMessage()
    {
        var actions = new RecordingMessageActions();

        await MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: NewRetryContext(5, 5),
            NullLogger.Instance, (_, _) => throw new TimeoutException(), CancellationToken.None);

        Assert.Equal(new[] { "abandon" }, actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenTransientWithoutRetryPolicy_AbandonsTheMessage()
    {
        var actions = new RecordingMessageActions();

        await MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: null,
            NullLogger.Instance, (_, _) => throw new TimeoutException(), CancellationToken.None);

        Assert.Equal(new[] { "abandon" }, actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenHostIsShuttingDown_LeavesTheMessageUnsettled()
    {
        var actions = new RecordingMessageActions();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: NewRetryContext(0, 5),
                NullLogger.Instance, (_, _) => throw new OperationCanceledException(), cts.Token));

        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenDeadLetterDescriptionIsOversized_TruncatesIt()
    {
        var actions = new RecordingMessageActions();
        // The description is composed from the correlation id; an oversized one must still be capped.
        var oversized = new string('x', 5000);

        await MessageSettlement.SettleAsync(NewMessage(correlationId: oversized), actions, retryContext: null,
            NullLogger.Instance, (_, _) => throw new JsonException("boom"), CancellationToken.None);

        Assert.NotNull(actions.DeadLetterDescription);
        Assert.True(actions.DeadLetterDescription!.Length <= 2048);
    }

    private static ServiceBusReceivedMessage NewMessage(string? correlationId = null) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "message-1",
            correlationId: correlationId,
            subject: "order.created.v1");

    private static RetryContext NewRetryContext(int retryCount, int maxRetryCount) =>
        new StubRetryContext(retryCount, maxRetryCount);

    private sealed class StubRetryContext(int retryCount, int maxRetryCount) : RetryContext
    {
        public override int RetryCount => retryCount;
        public override int MaxRetryCount => maxRetryCount;
    }

    // Shared with other function tests that need to satisfy the trigger signature.
    public sealed class StubFunctionContext : FunctionContext
    {
        public override string InvocationId => "test-invocation";
        public override string FunctionId => "test-function";
        public override TraceContext TraceContext => null!;
        public override BindingContext BindingContext => null!;
        public override RetryContext RetryContext => null!;
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition => null!;
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => null!;
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(string RenderedMessage, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception));
    }

    public sealed class RecordingMessageActions : ServiceBusMessageActions
    {
        public List<string> Calls { get; } = [];
        public string? DeadLetterReason { get; private set; }
        public string? DeadLetterDescription { get; private set; }

        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            Calls.Add("complete");
            return Task.CompletedTask;
        }

        public override Task AbandonMessageAsync(ServiceBusReceivedMessage message, IDictionary<string, object>? propertiesToModify = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("abandon");
            return Task.CompletedTask;
        }

        public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, Dictionary<string, object>? propertiesToModify = null, string? deadLetterReason = null, string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("deadletter");
            DeadLetterReason = deadLetterReason;
            DeadLetterDescription = deadLetterErrorDescription;
            return Task.CompletedTask;
        }
    }
}
