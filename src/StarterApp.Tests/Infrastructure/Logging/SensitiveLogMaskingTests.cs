using Microsoft.Extensions.Configuration;
using Serilog.Core;

namespace StarterApp.Tests.Infrastructure.Logging;

// Behavioural guard for sensitive-data masking: builds a logger through the SAME composition the
// API host uses (SerilogConfiguration.Apply) on top of the committed appsettings.json, logs an
// email address, and asserts the rendered output is masked. This catches both code drift (the
// masking enricher dropped from the pipeline) and config drift (a committed logging change that
// stops the masking from binding) — a wiring that merely compiles proves neither.
public class SensitiveLogMaskingTests
{
    [Fact]
    public void ApiLoggerComposition_WithCommittedAppSettings_MasksEmailAddressesInRenderedOutput()
    {
        var events = new List<LogEvent>();

        using (var logger = BuildApiLogger(events))
        {
            logger.Information("Customer contact updated to {Email}", "ada.lovelace@example.com");
        }

        var rendered = Assert.Single(events).RenderMessage();
        Assert.DoesNotContain("ada.lovelace@example.com", rendered);
        Assert.Contains("***MASKED***", rendered);
    }

    [Fact]
    public void ApiLoggerComposition_WithCommittedAppSettings_LeavesNonSensitiveContentReadable()
    {
        var events = new List<LogEvent>();

        using (var logger = BuildApiLogger(events))
        {
            logger.Information("Order {OrderId} moved to {Status}", 12345, "Shipped");
        }

        var rendered = Assert.Single(events).RenderMessage();
        Assert.Contains("12345", rendered);
        Assert.Contains("Shipped", rendered);
    }

    private static Logger BuildApiLogger(List<LogEvent> events)
    {
        var appSettingsPath = Path.Combine(FindRepoRoot(), "src", "StarterApp.Api", "appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(appSettingsPath).Build();
        var loggerConfiguration = new LoggerConfiguration().WriteTo.Sink(new CollectingSink(events));

        SerilogConfiguration.Apply(loggerConfiguration, configuration);

        return loggerConfiguration.CreateLogger();
    }

    private static string FindRepoRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "StarterApp.slnx")) ||
                    File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Repository root not found from the test working directory.");
    }

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
