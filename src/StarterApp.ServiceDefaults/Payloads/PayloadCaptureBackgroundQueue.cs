using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StarterApp.ServiceDefaults.Payloads;

public sealed record PendingPayloadAppend(string BlobName, string Line);

// Bounded in bytes, not items: an audit line carries the whole captured payload.
public sealed class PayloadCaptureBackgroundQueue
{
    public const string MeterName = "StarterApp.PayloadCapture";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DroppedCounter = Meter.CreateCounter<long>(
        "starterapp.payload_capture.background_writes_dropped",
        description: "Audit and entity-index lines dropped because the background queue was full or shutting down.");

    private readonly Channel<PendingPayloadAppend> _channel = Channel.CreateUnbounded<PendingPayloadAppend>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly long _maxBytes;
    private readonly ILogger<PayloadCaptureBackgroundQueue> _logger;
    private long _queuedBytes;
    private long _dropped;

    public PayloadCaptureBackgroundQueue(IOptions<PayloadCaptureOptions> options, ILogger<PayloadCaptureBackgroundQueue> logger)
    {
        _maxBytes = options.Value.BackgroundQueueMaxBytes;
        _logger = logger;
    }

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);

    internal ChannelReader<PendingPayloadAppend> Reader => _channel.Reader;

    public bool TryEnqueue(string blobName, string line)
    {
        var size = SizeOf(line);
        if (Interlocked.Add(ref _queuedBytes, size) > _maxBytes)
        {
            Interlocked.Add(ref _queuedBytes, -size);
            RecordDrop(blobName, "the queue is full");
            return false;
        }

        if (_channel.Writer.TryWrite(new PendingPayloadAppend(blobName, line)))
            return true;

        Interlocked.Add(ref _queuedBytes, -size);
        RecordDrop(blobName, "the queue has stopped accepting writes");
        return false;
    }

    internal void Dequeued(PendingPayloadAppend append)
    {
        Interlocked.Add(ref _queuedBytes, -SizeOf(append.Line));
    }

    internal void Complete()
    {
        _channel.Writer.TryComplete();
    }

    internal void RecordDrop(string blobName, string reason)
    {
        var dropped = Interlocked.Increment(ref _dropped);
        DroppedCounter.Add(1);
        _logger.LogWarning(
            "Dropped the background payload-capture write to {BlobName} because {Reason}; {DroppedCount} dropped since start. The archive record is unaffected",
            blobName,
            reason,
            dropped);
    }

    private static long SizeOf(string line) => (long)line.Length * sizeof(char);
}
