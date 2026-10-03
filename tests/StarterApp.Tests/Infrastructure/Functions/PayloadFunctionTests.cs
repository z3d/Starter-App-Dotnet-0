using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.Functions;
using StarterApp.ServiceDefaults.Payloads;
using StarterApp.Tests.Infrastructure.Payloads;
using MessageSettlementTests = StarterApp.Tests.Functions.MessageSettlementTests;

namespace StarterApp.Tests.Infrastructure.Functions;

public class PayloadFunctionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceBusFunction_WhenFailClosedArchiveRecovers_RetriesBeforeCompleting(bool inventory)
    {
        var store = new FailOnceArchiveStore();
        var sink = PayloadCaptureTests.CreateSink(store, DateTimeOffset.UtcNow,
            new PayloadCaptureOptions { ServiceBusFailureMode = PayloadCaptureFailureMode.FailClosed });
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), messageId: "retry-archive", correlationId: "retry-archive",
            contentType: "application/json");
        var actions = new MessageSettlementTests.RecordingMessageActions();

        var time = new ImmediateTimeProvider();
        if (inventory)
            await new InventoryReservationFunction(NullLogger<InventoryReservationFunction>.Instance, sink, time, new NullMessageInbox())
                .RunAsync(message, actions, CancellationToken.None);
        else
            await new OrderConfirmationEmailFunction(NullLogger<OrderConfirmationEmailFunction>.Instance, sink, time, new NullMessageInbox())
                .RunAsync(message, actions, CancellationToken.None);

        Assert.Equal(new[] { "complete" }, actions.Calls);
        var archive = Assert.Single(store.Inner.Lines, pair => pair.Key.StartsWith("archive/", StringComparison.Ordinal));
        Assert.Single(archive.Value);
        Assert.Equal(1, store.Failures);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, time.Waits);
    }

    // Backoff waits go through the host TimeProvider. This one records each wait and fires its
    // timer immediately, so the subscriber's real retry path runs without sleeping the suite.
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public List<TimeSpan> Waits { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Add(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FailOnceArchiveStore : IPayloadArchiveStore
    {
        public InMemoryPayloadArchiveStore Inner { get; } = new();
        public int Failures { get; private set; }
        public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
        {
            if (Failures == 0)
            {
                Failures++;
                throw new TimeoutException("Transient archive outage");
            }
            return Inner.AppendLineAsync(blobName, line, cancellationToken);
        }
        public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
            => Inner.DeleteOlderThanAsync(cutoffUtc, cancellationToken);
    }

    [Fact]
    public async Task ServiceBusFunction_ShouldCaptureInboundPayloadWithCorrelation()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = PayloadCaptureTests.CreateSink(store, timestamp);
        var function = new OrderConfirmationEmailFunction(new LoggerFactory().CreateLogger<OrderConfirmationEmailFunction>(), sink, TimeProvider.System, new NullMessageInbox());
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{"email":"ada@example.com","orderId":"abc"}"""),
            messageId: "message-1",
            subject: "order.created.v1",
            contentType: "application/json",
            correlationId: "case-789");

        var messageActions = new MessageSettlementTests.RecordingMessageActions();

        await function.RunAsync(message, messageActions, CancellationToken.None);

        Assert.Equal(new[] { "complete" }, messageActions.Calls);

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-789.jsonl");
        var entityIndexEntry = store.Lines.Single(pair => pair.Key == "entity-index/order/abc/2026-05-03/04/07/case-789.jsonl");
        Assert.Contains("\"channel\":\"servicebus\"", archiveEntry.Value.Single());
        Assert.Contains("ada@example.com", archiveEntry.Value.Single());
        Assert.Contains("\"archiveBlobName\":\"archive/2026-05-03/04/07/case-789.jsonl\"", entityIndexEntry.Value.Single());
        Assert.DoesNotContain("ada@example.com", entityIndexEntry.Value.Single());
    }

    [Fact]
    public async Task CleanupFunction_ShouldDeletePayloadBlobsOlderThanRetention()
    {
        var store = new InMemoryPayloadArchiveStore();
        await store.AppendLineAsync("archive/2026-05-01/00/00/case-old.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("audit/2026-05-01/00/00/payload-audit.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("entity-index/customer/42/2026-05-01/00/00/case-old.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("archive/2026-05-09/00/00/case-new.jsonl", "{}", CancellationToken.None);

        var options = Options.Create(new PayloadCaptureOptions { RetentionDays = 7 });
        var function = new PayloadArchiveCleanupFunction(
            store,
            new PayloadCaptureTests.FixedTimeProvider(new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero)),
            options,
            new NullJobRunRecorder(),
            new LoggerFactory().CreateLogger<PayloadArchiveCleanupFunction>());

        await function.RunAsync(null!, CancellationToken.None);

        Assert.DoesNotContain("archive/2026-05-01/00/00/case-old.jsonl", store.Lines.Keys);
        Assert.DoesNotContain("audit/2026-05-01/00/00/payload-audit.jsonl", store.Lines.Keys);
        Assert.DoesNotContain("entity-index/customer/42/2026-05-01/00/00/case-old.jsonl", store.Lines.Keys);
        Assert.Contains("archive/2026-05-09/00/00/case-new.jsonl", store.Lines.Keys);
    }

    [Fact]
    public async Task CleanupFunction_CountsEachRunOnTheScheduledJobMeter()
    {
        var counted = new List<(string Job, string Outcome)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobRunMetrics.MeterName && instrument.Name == JobRunMetrics.RunsInstrument)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var tagged = tags.ToArray().ToDictionary(tag => tag.Key, tag => (string)tag.Value!);
            lock (counted)
                counted.Add((tagged["job"], tagged["outcome"]));
        });
        listener.Start();
        var function = new PayloadArchiveCleanupFunction(
            new InMemoryPayloadArchiveStore(),
            new PayloadCaptureTests.FixedTimeProvider(new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero)),
            Options.Create(new PayloadCaptureOptions()),
            new NullJobRunRecorder(),
            new LoggerFactory().CreateLogger<PayloadArchiveCleanupFunction>());

        await function.RunAsync(null!, CancellationToken.None);

        Assert.Contains((PayloadArchiveCleanupFunction.JobName, "Succeeded"), counted);
    }

    [Fact]
    public async Task CleanupFunction_RecordsACancelledRunAsCancelled()
    {
        var recorder = new ListRecorder();
        using var cancelled = new CancellationTokenSource();
        var function = new PayloadArchiveCleanupFunction(
            new CancellingArchiveStore(cancelled),
            new PayloadCaptureTests.FixedTimeProvider(new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero)),
            Options.Create(new PayloadCaptureOptions()),
            recorder,
            new LoggerFactory().CreateLogger<PayloadArchiveCleanupFunction>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => function.RunAsync(null!, cancelled.Token));

        Assert.Equal([(JobOutcomes.Cancelled, false)], recorder.Completed);
    }

    private sealed class CancellingArchiveStore(CancellationTokenSource source) : IPayloadArchiveStore
    {
        public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
        {
            await source.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class ListRecorder : IJobRunRecorder
    {
        public List<(string Outcome, bool TokenCancelled)> Completed { get; } = [];

        public Task<Guid> StartRunAsync(string jobName, DateTimeOffset startedOnUtc, CancellationToken cancellationToken) => Task.FromResult(Guid.CreateVersion7());

        public Task CompleteRunAsync(Guid runId, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken)
        {
            Completed.Add((outcome, cancellationToken.IsCancellationRequested));
            return Task.CompletedTask;
        }

        public Task RecordRunAsync(string jobName, DateTimeOffset startedOnUtc, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
