namespace StarterApp.Tests;

public static class TestLoggerConfiguration
{
    public static void ConfigureTestLogging(ITestOutputHelper output, LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.Console()
            .WriteTo.TestOutput(output)
            .Enrich.FromLogContext()
            .CreateLogger();
    }
}
