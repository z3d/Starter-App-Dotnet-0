using System.Globalization;

namespace StarterApp.DbMigrator;

public sealed record MigrationSessionOptions(TimeSpan LockTimeout, TimeSpan StatementTimeout)
{
    public static MigrationSessionOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(15));

    public static MigrationSessionOptions From(IConfiguration configuration) => new(
        Read(configuration, "Migrator:LockTimeout", Default.LockTimeout),
        Read(configuration, "Migrator:StatementTimeout", Default.StatementTimeout));

    private static TimeSpan Read(IConfiguration configuration, string key, TimeSpan fallback)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : throw new InvalidOperationException($"{key} must be a positive time span such as 00:00:05, not '{value}'.");
    }
}
