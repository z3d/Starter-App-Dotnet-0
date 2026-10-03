using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StarterApp.ServiceDefaults.Payloads;

// Drains in StoppedAsync, after the web server and outbox processor have made their last capture.
public sealed class PayloadCaptureBackgroundWriter : IHostedLifecycleService, IDisposable
{
    private readonly PayloadCaptureBackgroundQueue _queue;
    private readonly IPayloadArchiveStore _store;
    private readonly ILogger<PayloadCaptureBackgroundWriter> _logger;
    private readonly CancellationTokenSource _abandon = new();
    private readonly TimeSpan _writeTimeout;
    private Task? _drain;

    public PayloadCaptureBackgroundWriter(PayloadCaptureBackgroundQueue queue, IPayloadArchiveStore store, IOptions<PayloadCaptureOptions> options, ILogger<PayloadCaptureBackgroundWriter> logger)
    {
        _writeTimeout = TimeSpan.FromSeconds(options.Value.BackgroundWriteTimeoutSeconds);
        _queue = queue;
        _store = store;
        _logger = logger;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _drain = Task.Run(DrainAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();
        if (_drain is null)
            return;

        await using var registration = cancellationToken.Register(_abandon.Cancel);
        await _drain;
    }

    private async Task DrainAsync()
    {
        await foreach (var append in _queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            _queue.Dequeued(append);
            if (_abandon.IsCancellationRequested)
            {
                _queue.RecordDrop(append.BlobName, "the host's shutdown timeout ran out");
                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_abandon.Token);
            timeout.CancelAfter(_writeTimeout);
            try
            {
                // WaitAsync as well: a store that ignores its token must not hold the one reader.
                await _store.AppendLineAsync(append.BlobName, append.Line, timeout.Token).WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (_abandon.IsCancellationRequested)
            {
                _queue.RecordDrop(append.BlobName, "the host's shutdown timeout ran out");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _queue.RecordDrop(append.BlobName, "the write timed out");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background payload-capture write to {BlobName} failed; the archive record is unaffected", append.BlobName);
            }
        }
    }

    public void Dispose()
    {
        _abandon.Dispose();
    }
}
