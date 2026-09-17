using DbUp.Engine.Output;

namespace StarterApp.DbMigrator;

// Routes DbUp's own output (database creation, each script as it runs, the failing statement)
// through the migrator's Serilog pipeline, so a deployment log shows what was executing when a
// migration failed instead of only the final error. DbUp logs script and database names, never
// the connection string.
public sealed class SerilogUpgradeLog(ILogger logger) : IUpgradeLog
{
    public void LogTrace(string format, params object[] args) => logger.Verbose(format, args);

    public void LogDebug(string format, params object[] args) => logger.Debug(format, args);

    public void LogInformation(string format, params object[] args) => logger.Information(format, args);

    public void LogWarning(string format, params object[] args) => logger.Warning(format, args);

    public void LogError(string format, params object[] args) => logger.Error(format, args);

    public void LogError(Exception ex, string format, params object[] args) => logger.Error(ex, format, args);
}
