using Serilog.Core;

namespace StarterApp.Tests.Infrastructure.Logging;

public class SerilogUpgradeLogTests
{
    [Fact]
    public void ForwardsEachDbUpLevel_WithArgumentsAndException()
    {
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new ListSink(events))
            .CreateLogger();
        var upgradeLog = new SerilogUpgradeLog(logger);
        var failure = new InvalidOperationException("boom");

        upgradeLog.LogTrace("trace {0}", 1);
        upgradeLog.LogDebug("debug {0}", 2);
        upgradeLog.LogInformation("Executing Database Server script '{0}'", "0007_Example.sql");
        upgradeLog.LogWarning("warning {0}", 3);
        upgradeLog.LogError("error {0}", 4);
        upgradeLog.LogError(failure, "failed {0}", 5);

        Assert.Equal(
            [LogEventLevel.Verbose, LogEventLevel.Debug, LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error, LogEventLevel.Error],
            events.Select(e => e.Level));
        Assert.Contains("0007_Example.sql", events[2].RenderMessage(), StringComparison.Ordinal);
        Assert.Same(failure, events[5].Exception);
    }

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
