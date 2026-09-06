using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StarterApp.Functions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace StarterApp.Tests.Functions;

public class MessageSettlementTests
{
    [Fact]
    public async Task SettleAsync_TransientFailureWithoutHostRetryContext_RetriesHandlerBeforeSettlement()
    {
        var actions = new RecordingMessageActions();
        var attempts = 0;

        await MessageSettlement.SettleAsync(NewMessage(), actions, NullLogger.Instance,
            (_, _) => ++attempts == 1 ? throw new TimeoutException() : Task.CompletedTask, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(new[] { "complete" }, actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenTransientFailuresPersist_BacksOffThenAbandonsOnce()
    {
        var actions = new RecordingMessageActions();
        var delays = new List<TimeSpan>();
        var attempts = 0;
        await RunWithRecordedDelaysAsync(actions, (_, _) =>
        {
            attempts++;
            throw new TimeoutException();
        }, delays);

        Assert.Equal(6, attempts);
        Assert.Equal(new[] { 5, 10, 20, 40, 45 }, delays.Select(delay => (int)delay.TotalSeconds));
        Assert.Equal(new[] { "abandon" }, actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenHandlerSucceeds_CompletesTheMessage()
    {
        var actions = new RecordingMessageActions();
        var delays = new List<TimeSpan>();
        await RunWithRecordedDelaysAsync(actions, (_, _) => Task.CompletedTask, delays);
        Assert.Equal(new[] { "complete" }, actions.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task SettleAsync_WhenCompletionFails_DoesNotRepeatSuccessfulHandlerWork()
    {
        var actions = new RecordingMessageActions { CompletionFailure = new TimeoutException() };
        var delays = new List<TimeSpan>();
        var attempts = 0;
        await Assert.ThrowsAsync<TimeoutException>(() => RunWithRecordedDelaysAsync(actions, (_, _) =>
        {
            attempts++;
            return Task.CompletedTask;
        }, delays));

        Assert.Equal(1, attempts);
        Assert.Equal(new[] { "complete" }, actions.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task SettleAsync_WhenFailureIsNonRetryable_DeadLettersWithoutRetryOrPayloadText()
    {
        var actions = new RecordingMessageActions();
        var delays = new List<TimeSpan>();
        await RunWithRecordedDelaysAsync(actions, (_, _) => throw new JsonException("payload contains user@example.com"), delays);
        Assert.Empty(delays);
        Assert.Equal(new[] { "deadletter" }, actions.Calls);
        Assert.Equal(nameof(JsonException), actions.DeadLetterReason);
        Assert.Contains("corr-42", actions.DeadLetterDescription);
        Assert.DoesNotContain("user@example.com", actions.DeadLetterDescription);
    }

    [Fact]
    public async Task SettleAsync_LogsNeverCarryTheExceptionObjectOrItsMessageText()
    {
        const string sentinel = "payload contains user@example.com";
        var logger = new RecordingLogger();
        await RunWithRecordedDelaysAsync(new RecordingMessageActions(), (_, _) => throw new JsonException(sentinel), [], logger);
        await RunWithRecordedDelaysAsync(new RecordingMessageActions(), (_, _) => throw new TimeoutException(sentinel), [], logger);
        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(sentinel, entry.RenderedMessage);
        });
    }

    [Fact]
    public async Task SettleAsync_WhenHostIsShuttingDown_LeavesTheMessageUnsettled()
    {
        var actions = new RecordingMessageActions();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MessageSettlement.SettleAsync(
            NewMessage(), actions, NullLogger.Instance, (_, _) => throw new InvalidOperationException("Handler must not run"), cts.Token));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenCancelledDuringBackoff_LeavesTheMessageUnsettled()
    {
        var actions = new RecordingMessageActions();
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MessageSettlement.SettleAsync(
            NewMessage(), actions, NullLogger.Instance, (_, _) => throw new TimeoutException(),
            async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
            }, MessageSettlement.ExecutionTimeout, cts.Token));
        Assert.Empty(actions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettleAsync_DeadlineIncludesHandlerWorkAndBackoff(bool blockInBackoff)
    {
        var actions = new RecordingMessageActions();
        var attempts = 0;
        var task = MessageSettlement.SettleAsync(NewMessage(), actions, NullLogger.Instance,
            (_, token) =>
            {
                attempts++;
                return blockInBackoff ? throw new TimeoutException() : Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token), TimeSpan.FromMilliseconds(50), CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, attempts);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenCancellationWasIgnoredByHandler_DoesNotComplete()
    {
        var actions = new RecordingMessageActions();
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MessageSettlement.SettleAsync(
            NewMessage(), actions, NullLogger.Instance, (_, _) => cts.CancelAsync(), cts.Token));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenDeadLetterDescriptionIsOversized_TruncatesIt()
    {
        var actions = new RecordingMessageActions();
        await MessageSettlement.SettleAsync(NewMessage(new string('x', 5000)), actions,
            NullLogger.Instance, (_, _) => throw new JsonException("boom"), CancellationToken.None);
        Assert.NotNull(actions.DeadLetterDescription);
        Assert.True(actions.DeadLetterDescription.Length <= 2048);
    }

    private static Task RunWithRecordedDelaysAsync(RecordingMessageActions actions,
        Func<ServiceBusReceivedMessage, CancellationToken, Task> handler, List<TimeSpan> delays, ILogger? logger = null)
        => MessageSettlement.SettleAsync(NewMessage(), actions, logger ?? NullLogger.Instance, handler,
            (delay, token) =>
            {
                token.ThrowIfCancellationRequested();
                delays.Add(delay);
                return Task.CompletedTask;
            }, MessageSettlement.ExecutionTimeout, CancellationToken.None);

    private static ServiceBusReceivedMessage NewMessage(string correlationId = "corr-42") =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("{}"), messageId: "message-1",
            correlationId: correlationId, subject: "order.created.v1");

    private sealed class RecordingLogger : ILogger
    {
        public List<(string RenderedMessage, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((formatter(state, exception), exception));
    }

    public sealed class RecordingMessageActions : ServiceBusMessageActions
    {
        public List<string> Calls { get; } = [];
        public string? DeadLetterReason { get; private set; }
        public string? DeadLetterDescription { get; private set; }
        public Exception? CompletionFailure { get; init; }
        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            Calls.Add("complete");
            return CompletionFailure is null ? Task.CompletedTask : Task.FromException(CompletionFailure);
        }
        public override Task AbandonMessageAsync(ServiceBusReceivedMessage message, IDictionary<string, object>? propertiesToModify = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("abandon");
            return Task.CompletedTask;
        }
        public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, Dictionary<string, object>? propertiesToModify = null,
            string? deadLetterReason = null, string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("deadletter");
            DeadLetterReason = deadLetterReason;
            DeadLetterDescription = deadLetterErrorDescription;
            return Task.CompletedTask;
        }
    }
}
