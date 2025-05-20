using Serilog;
using Serilog.Events;
using Xunit.Abstractions;

namespace DockerLearningApi.Tests;

public static class TestLoggerConfiguration
{
    public static ILogger CreateLogger(ITestOutputHelper output, LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.Console()
            .WriteTo.File("logs/tests-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.TestOutput(output)
            .Enrich.FromLogContext()
            .CreateLogger();
    }

    public static ILogger CreateLogger(LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.Console()
            .WriteTo.File("logs/tests-.log", rollingInterval: RollingInterval.Day)
            .Enrich.FromLogContext()
            .CreateLogger();
    }
}