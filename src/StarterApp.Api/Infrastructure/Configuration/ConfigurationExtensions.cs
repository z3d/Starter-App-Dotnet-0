namespace StarterApp.Api.Infrastructure.Configuration;

public static class ConfigurationExtensions
{
    private static readonly string[] DefaultConnectionKeys =
        ["database", "DockerLearning", "sqlserver", "DefaultConnection"];

    public static string GetRequiredConnectionString(this IConfiguration configuration, params string[] keys)
    {
        var toCheck = (keys is { Length: > 0 }) ? keys : DefaultConnectionKeys;

        foreach (var key in toCheck)
        {
            var value = configuration.GetConnectionString(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        throw new InvalidOperationException($"No connection string found. Checked: {string.Join(", ", toCheck)}.");
    }
}

