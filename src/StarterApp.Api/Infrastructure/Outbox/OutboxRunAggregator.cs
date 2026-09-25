namespace StarterApp.Api.Infrastructure.Outbox;

internal sealed class OutboxRunAggregator
{
    private readonly TimeSpan _interval;
    private DateTimeOffset _windowStartUtc;
    private int _published;
    private int _errored;
    private int _retried;
    private int _purged;
    private int _paused;

    public OutboxRunAggregator(TimeSpan interval, DateTimeOffset nowUtc)
    {
        _interval = interval;
        _windowStartUtc = nowUtc;
    }

    public void AddPublished() => _published++;
    public void AddErrored() => _errored++;
    public void AddRetried() => _retried++;
    public void AddPurged(int count) => _purged += count;

    // Counted so a stalled outbox is distinguishable from an idle one.
    public void AddPaused() => _paused++;

    public OutboxHealthWindow? TryFlush(DateTimeOffset nowUtc)
    {
        if (nowUtc - _windowStartUtc < _interval)
            return null;

        if (_published == 0 && _errored == 0 && _retried == 0 && _purged == 0 && _paused == 0)
        {
            // Idle window: advance without emitting a row.
            _windowStartUtc = nowUtc;
            return null;
        }

        var window = new OutboxHealthWindow(_windowStartUtc, nowUtc, _published, _errored, _retried, _purged, _paused);
        _published = 0;
        _errored = 0;
        _retried = 0;
        _purged = 0;
        _paused = 0;
        _windowStartUtc = nowUtc;
        return window;
    }
}

internal sealed record OutboxHealthWindow(
    DateTimeOffset StartedOnUtc,
    DateTimeOffset CompletedOnUtc,
    int Published,
    int Errored,
    int Retried,
    int Purged,
    int Paused)
{
    // Throttling that still publishes stays Succeeded; a window that paused and published nothing is a stall.
    public string Outcome => Errored > 0 || (Paused > 0 && Published == 0) ? "Degraded" : "Succeeded";

    public string ToSummaryJson() =>
        $"{{\"published\":{Published},\"errored\":{Errored},\"retried\":{Retried},\"purged\":{Purged},\"paused\":{Paused}}}";
}
