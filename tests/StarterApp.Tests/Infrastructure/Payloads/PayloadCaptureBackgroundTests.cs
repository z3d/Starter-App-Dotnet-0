using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class PayloadCaptureBackgroundTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);

    private const string TwelveIds = """
        {"customerId":"c-1","orderId":"o-1","productId":"p-1","itemId":"i-1","invoiceId":"v-1","accountId":"a-1",
         "cartId":"k-1","couponId":"u-1","supplierId":"s-1","shipmentId":"h-1","warehouseId":"w-1","batchId":"b-1"}
        """;

    [Fact]
    public async Task ARequestWithTwelveEntityIds_TouchesTheStoreTwiceBeforeItReturns_AndShutdownFlushesTheRest()
    {
        var store = new CountingStore();
        var queue = CreateQueue();
        var sink = CreateSink(store, queue, new PayloadCaptureOptions());

        var inbound = await sink.CaptureAsync(Request("inbound", PayloadCaptureChannels.Http), CancellationToken.None);
        var outbound = await sink.CaptureAsync(Request("outbound", PayloadCaptureChannels.Http), CancellationToken.None);

        Assert.NotNull(inbound);
        Assert.NotNull(outbound);
        Assert.Equal(12, inbound.EntityReferences.Count);
        Assert.Equal(2, store.Calls);
        Assert.Equal(2, store.Inner.Lines.Single().Value.Count);

        using var writer = CreateWriter(queue, store);
        await writer.StartAsync(CancellationToken.None);
        await writer.StoppedAsync(CancellationToken.None);

        Assert.Equal(2 + 2 + 24, store.Calls);
        Assert.Equal(2, store.Inner.Lines["audit/2026-05-03/04/07/payload-audit.jsonl"].Count);
        var entityIndex = store.Inner.Lines.Where(pair => pair.Key.StartsWith("entity-index/", StringComparison.Ordinal)).ToList();
        Assert.Equal(12, entityIndex.Count);
        Assert.All(entityIndex, pair => Assert.Equal(2, pair.Value.Count));
        Assert.Equal(0, queue.DroppedCount);
        Assert.Equal(0, queue.QueuedBytes);
    }

    [Fact]
    public async Task AFailClosedChannel_KeepsItsEntityIndexInline_SoAFailedIndexWriteStillFailsTheCapture()
    {
        var store = new CountingStore(failPrefix: "entity-index/");
        var queue = CreateQueue();
        var sink = CreateSink(store, queue, new PayloadCaptureOptions { ServiceBusFailureMode = PayloadCaptureFailureMode.FailClosed });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sink.CaptureAsync(Request("outbound", PayloadCaptureChannels.ServiceBus), CancellationToken.None));

        Assert.Contains("archive/2026-05-03/04/07/case-123.jsonl", store.Inner.Lines.Keys);
    }

    [Fact]
    public async Task AFailedBackgroundWrite_IsLoggedAndTheRestStillLand()
    {
        var store = new CountingStore(failPrefix: "audit/");
        var queue = CreateQueue();
        var sink = CreateSink(store, queue, new PayloadCaptureOptions());

        var record = await sink.CaptureAsync(Request("inbound", PayloadCaptureChannels.Http), CancellationToken.None);

        using var writer = CreateWriter(queue, store);
        await writer.StartAsync(CancellationToken.None);
        await writer.StoppedAsync(CancellationToken.None);

        Assert.NotNull(record);
        Assert.DoesNotContain(store.Inner.Lines.Keys, key => key.StartsWith("audit/", StringComparison.Ordinal));
        Assert.Equal(12, store.Inner.Lines.Keys.Count(key => key.StartsWith("entity-index/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WhenTheQueueIsFull_TheWriteIsDroppedAndCounted_AndTheArchiveRecordIsStillWritten()
    {
        var store = new CountingStore();
        var options = new PayloadCaptureOptions { BackgroundQueueMaxBytes = 1_048_576 };
        var queue = CreateQueue(options);
        var sink = CreateSink(store, queue, options);
        var request = Request("inbound", PayloadCaptureChannels.Http,
            TwelveIds.TrimEnd()[..^1] + $$""","remark":"{{new string('a', 600_000)}}"}""");

        var record = await sink.CaptureAsync(request, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal(1, store.Calls);
        Assert.Equal(1, queue.DroppedCount);
        Assert.InRange(queue.QueuedBytes, 1, options.BackgroundQueueMaxBytes);
    }

    [Fact]
    public async Task AWriteQueuedAfterShutdown_IsDroppedAndCounted()
    {
        var store = new CountingStore();
        var queue = CreateQueue();
        using var writer = CreateWriter(queue, store);
        await writer.StartAsync(CancellationToken.None);
        await writer.StoppedAsync(CancellationToken.None);

        Assert.False(queue.TryEnqueue("audit/2026-05-03/04/07/payload-audit.jsonl", "{}"));
        Assert.Equal(1, queue.DroppedCount);
        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWriteThatHangs_TimesOutAndIsCounted_AndTheLinesBehindItStillLand(bool storeHonoursItsToken)
    {
        var store = new HangingStore(hangOn: "audit/hung.jsonl", storeHonoursItsToken);
        var queue = CreateQueue();
        queue.TryEnqueue("audit/hung.jsonl", "{}");
        queue.TryEnqueue("audit/after.jsonl", "{}");

        using var writer = CreateWriter(queue, store, new PayloadCaptureOptions { BackgroundWriteTimeoutSeconds = 1 });
        await writer.StartAsync(CancellationToken.None);
        await writer.StoppedAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(["audit/after.jsonl"], store.Inner.Lines.Keys);
        Assert.Equal(1, queue.DroppedCount);
        Assert.Equal(0, queue.QueuedBytes);
    }

    private static PayloadCaptureBackgroundWriter CreateWriter(PayloadCaptureBackgroundQueue queue, IPayloadArchiveStore store, PayloadCaptureOptions? options = null) =>
        new(queue, store, Options.Create(options ?? new PayloadCaptureOptions()), NullLogger<PayloadCaptureBackgroundWriter>.Instance);

    private sealed class HangingStore(string hangOn, bool honoursItsToken) : IPayloadArchiveStore
    {
        public InMemoryPayloadArchiveStore Inner { get; } = new();

        public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
        {
            if (blobName != hangOn)
                return Inner.AppendLineAsync(blobName, line, cancellationToken);

            return honoursItsToken ? Task.Delay(Timeout.Infinite, cancellationToken) : new TaskCompletionSource().Task;
        }

        public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken) =>
            Inner.DeleteOlderThanAsync(cutoffUtc, cancellationToken);
    }

    private static PayloadCaptureRequest Request(string direction, string channel, string payload = TwelveIds) => new()
    {
        CorrelationId = "case-123",
        Direction = direction,
        Channel = channel,
        Operation = "POST /api/v1/things",
        ContentType = "application/json",
        Payload = payload
    };

    private static PayloadCaptureBackgroundQueue CreateQueue(PayloadCaptureOptions? options = null) =>
        new(Options.Create(options ?? new PayloadCaptureOptions()), NullLogger<PayloadCaptureBackgroundQueue>.Instance);

    private static PayloadCaptureSink CreateSink(IPayloadArchiveStore store, PayloadCaptureBackgroundQueue queue, PayloadCaptureOptions captureOptions)
    {
        var options = Options.Create(captureOptions);
        return new PayloadCaptureSink(
            store,
            new JsonPayloadRedactor(options),
            new PayloadCaptureTests.FixedTimeProvider(Timestamp),
            options,
            NullLogger<PayloadCaptureSink>.Instance,
            queue);
    }

    private sealed class CountingStore(string? failPrefix = null) : IPayloadArchiveStore
    {
        private int _calls;

        public InMemoryPayloadArchiveStore Inner { get; } = new();

        public int Calls => Volatile.Read(ref _calls);

        public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (failPrefix is not null && blobName.StartsWith(failPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("blob unavailable");

            return Inner.AppendLineAsync(blobName, line, cancellationToken);
        }

        public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken) =>
            Inner.DeleteOlderThanAsync(cutoffUtc, cancellationToken);
    }
}
