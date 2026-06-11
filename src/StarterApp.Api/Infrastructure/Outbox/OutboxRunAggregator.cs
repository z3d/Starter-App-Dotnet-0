namespace StarterApp.Api.Infrastructure.Outbox;

// Accumulates per-message outcomes into periodic health windows so the outbox writes one
// job_runs row per interval with activity — never one per message, never noise for idle
// intervals. Pure logic, unit-tested directly; OutboxProcessor drives it.
internal sealed class OutboxRunAggregator
{
    private readonly TimeSpan _interval;
    private DateTimeOffset _windowStartUtc;
    private int _published;
    private int _errored;
    private int _retried;
    private int _purged;

    public OutboxRunAggregator(TimeSpan interval, DateTimeOffset nowUtc)
    {
        _interval = interval;
        _windowStartUtc = nowUtc;
    }

    public void AddPublished() => _published++;
    public void AddErrored() => _errored++;
    public void AddRetried() => _retried++;
    public void AddPurged(int count) => _purged += count;

    public OutboxHealthWindow? TryFlush(DateTimeOffset nowUtc)
    {
        if (nowUtc - _windowStartUtc < _interval)
            return null;

        if (_published == 0 && _errored == 0 && _retried == 0 && _purged == 0)
        {
            // Idle window: advance without emitting a row.
            _windowStartUtc = nowUtc;
            return null;
        }

        var window = new OutboxHealthWindow(_windowStartUtc, nowUtc, _published, _errored, _retried, _purged);
        _published = 0;
        _errored = 0;
        _retried = 0;
        _purged = 0;
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
    int Purged)
{
    public string Outcome => Errored == 0 ? "Succeeded" : "Degraded";

    public string ToSummaryJson() =>
        $"{{\"published\":{Published},\"errored\":{Errored},\"retried\":{Retried},\"purged\":{Purged}}}";
}
