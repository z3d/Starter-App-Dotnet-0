namespace StarterApp.Tests.Infrastructure;

public class OutboxRunAggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryFlush_BeforeIntervalElapses_ReturnsNothing()
    {
        var aggregator = new OutboxRunAggregator(TimeSpan.FromMinutes(15), T0);
        aggregator.AddPublished();

        Assert.Null(aggregator.TryFlush(T0.AddMinutes(14)));
    }

    [Fact]
    public void TryFlush_AfterInterval_WithActivity_EmitsWindowAndResets()
    {
        var aggregator = new OutboxRunAggregator(TimeSpan.FromMinutes(15), T0);
        aggregator.AddPublished();
        aggregator.AddPublished();
        aggregator.AddErrored();
        aggregator.AddRetried();
        aggregator.AddPurged(7);

        var window = aggregator.TryFlush(T0.AddMinutes(15));

        Assert.NotNull(window);
        Assert.Equal(T0, window.StartedOnUtc);
        Assert.Equal(2, window.Published);
        Assert.Equal(1, window.Errored);
        Assert.Equal(1, window.Retried);
        Assert.Equal(7, window.Purged);
        Assert.Equal("Degraded", window.Outcome);
        Assert.Equal("{\"published\":2,\"errored\":1,\"retried\":1,\"purged\":7,\"paused\":0}", window.ToSummaryJson());

        // Counters reset; the next interval with no activity emits nothing.
        Assert.Null(aggregator.TryFlush(T0.AddMinutes(30)));
    }

    [Fact]
    public void TryFlush_AfterInterval_WithoutActivity_AdvancesWindowSilently()
    {
        var aggregator = new OutboxRunAggregator(TimeSpan.FromMinutes(15), T0);

        Assert.Null(aggregator.TryFlush(T0.AddMinutes(16)));

        // Window advanced: activity in the new window flushes from the advanced start.
        aggregator.AddPublished();
        var window = aggregator.TryFlush(T0.AddMinutes(31));
        Assert.NotNull(window);
        Assert.Equal(T0.AddMinutes(16), window.StartedOnUtc);
        Assert.Equal("Succeeded", window.Outcome);
    }

    [Fact]
    public void TryFlush_AfterInterval_WithOnlyPausedBatches_EmitsDegradedWindow()
    {
        // A batch that pauses on a dependency fault publishes nothing and errors nothing. Without a
        // paused count, hours of a fully stalled outbox looked identical to an idle one.
        var aggregator = new OutboxRunAggregator(TimeSpan.FromMinutes(15), T0);
        aggregator.AddPaused();
        aggregator.AddPaused();

        var window = aggregator.TryFlush(T0.AddMinutes(15));

        Assert.NotNull(window);
        Assert.Equal(2, window.Paused);
        Assert.Equal(0, window.Published);
        Assert.Equal("Degraded", window.Outcome);
        Assert.Contains("\"paused\":2", window.ToSummaryJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryFlush_WithPausesButPublishes_StaysSucceeded()
    {
        // Routine throttling pauses a batch and the next poll publishes; that is not a degraded
        // window, only a fully stalled one (paused, nothing published) is.
        var aggregator = new OutboxRunAggregator(TimeSpan.FromMinutes(15), T0);
        aggregator.AddPaused();
        aggregator.AddPublished();

        var window = aggregator.TryFlush(T0.AddMinutes(15));

        Assert.NotNull(window);
        Assert.Equal("Succeeded", window.Outcome);
        Assert.Equal(1, window.Paused);
    }
}
