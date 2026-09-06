using System.Globalization;
using System.Text.Json;

namespace StarterApp.AppHost.Tests;

public class FunctionsHostConfigConventionTests
{
    [Fact]
    public void HandlerRetryDeadline_MustFitInsideLockRenewal_WithoutUnsupportedHostPolicy()
    {
        var hostJsonPath = Path.Combine(FindRepoRoot(), "src", "StarterApp.Functions", "host.json");
        using var document = JsonDocument.Parse(File.ReadAllText(hostJsonPath));
        Assert.False(document.RootElement.TryGetProperty("retry", out _),
            "Service Bus does not support Functions execution-retry policies; retry handler work explicitly.");
        var lockRenewal = TimeSpan.Parse(
            document.RootElement.GetProperty("extensions").GetProperty("serviceBus").GetProperty("maxAutoLockRenewalDuration").GetString()!,
            CultureInfo.InvariantCulture);
        Assert.True(StarterApp.Functions.MessageSettlement.ExecutionTimeout <= lockRenewal * 0.8,
            "The total handler/retry deadline must leave settlement and lock-renewal headroom.");
    }

    // %setting% trigger lookups resolve against IConfiguration, where the environment-variable
    // provider has already normalized '__' to ':'. A literal '__' inside %...% therefore resolves
    // to null on a real Functions host, fails that function's indexing, and can take down every
    // other trigger in the same worker.
    [Fact]
    public void TriggerSettingExpressions_MustUseConfigurationKeyForm()
    {
        var offenders = typeof(StarterApp.Functions.PayloadArchiveCleanupFunction).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .SelectMany(method => method.GetParameters())
            .SelectMany(parameter => parameter.GetCustomAttributes(inherit: false)
                .Where(attribute => attribute.GetType().Name.EndsWith("TriggerAttribute", StringComparison.Ordinal))
                .SelectMany(attribute => attribute.GetType().GetProperties()
                    .Where(property => property.PropertyType == typeof(string))
                    .Select(property => (string?)property.GetValue(attribute))
                    .Where(value => value is not null && value.Contains('%', StringComparison.Ordinal) && value.Contains("__", StringComparison.Ordinal))
                    .Select(value => $"{parameter.Member.DeclaringType?.Name}.{parameter.Member.Name}({parameter.Name}): '{value}'")))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Trigger %setting% expressions must use ':' configuration keys, not '__' env-var names:\n" +
            string.Join("\n", offenders));
    }

    private static string FindRepoRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Repository root not found from test execution directory.");
    }
}
