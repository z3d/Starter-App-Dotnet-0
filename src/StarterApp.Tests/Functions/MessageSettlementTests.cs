using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using StarterApp.Functions;

namespace StarterApp.Tests.Functions;

public class MessageSettlementTests
{
    [Fact]
    public async Task SettleAsync_WhenHandlerSucceeds_CompletesTheMessage()
    {
        var actions = new RecordingMessageActions();

        await MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: null,
            NullLogger.Instance, (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(new[] { "complete" }, actions.Calls);
    }

    [Fact]
    public async Task SettleAsync_WhenFailureIsNonRetryable_DeadLettersWithReasonAndDescription()
    {
        var actions = new RecordingMessageActions();

        await MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: NewRetryContext(0, 5),
            NullLogger.Instance, (_, _) => throw new JsonException("unparseable payload"), CancellationToken.None);

        Assert.Equal(new[] { "deadletter" }, actions.Calls);
        Assert.Equal(nameof(JsonException), actions.DeadLetterReason);
        Assert.Contains("unparseable payload", actions.DeadLetterDescription);
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
        var oversized = new string('x', 5000);

        await MessageSettlement.SettleAsync(NewMessage(), actions, retryContext: null,
            NullLogger.Instance, (_, _) => throw new JsonException(oversized), CancellationToken.None);

        Assert.NotNull(actions.DeadLetterDescription);
        Assert.True(actions.DeadLetterDescription!.Length <= 2048);
    }

    private static ServiceBusReceivedMessage NewMessage() =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "message-1",
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
