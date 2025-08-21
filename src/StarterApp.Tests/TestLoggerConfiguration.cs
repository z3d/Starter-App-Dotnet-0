namespace StarterApp.Tests;

public static class TestLoggerConfiguration
{
    private static bool _isConfigured = false;

    public static void ConfigureStaticLogging(LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        if (!_isConfigured)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .WriteTo.Console()
                .WriteTo.File("logs/tests-.log", rollingInterval: RollingInterval.Day)
                .Enrich.FromLogContext()
                .CreateLogger();

            _isConfigured = true;
        }
    }

    public static void ConfigureTestLogging(ITestOutputHelper output, LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.Console()
            .WriteTo.File("logs/tests-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.TestOutput(output)
            .Enrich.FromLogContext()
            .CreateLogger();
    }

    // Keep these methods for backward compatibility if needed
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