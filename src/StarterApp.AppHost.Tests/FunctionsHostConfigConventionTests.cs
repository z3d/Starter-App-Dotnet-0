using System.Globalization;
using System.Text.Json;

namespace StarterApp.AppHost.Tests;

public class FunctionsHostConfigConventionTests
{
    // Functions run with PayloadCapture FailClosed, so a capture (archive) failure throws and the
    // message is abandoned. Without a host-level retry policy, Service Bus redelivers immediately
    // and MaxDeliveryCount burns in seconds — a seconds-long blob outage dead-letters live events.
    // The publish side deliberately pauses with retry budget intact; the consume side must back off
    // too. The retry window must stay inside maxAutoLockRenewalDuration so the lock survives retries.
    [Fact]
    public void HostJson_MustDefineBackoffRetryWithinLockRenewalWindow()
    {
        var hostJsonPath = Path.Combine(FindRepoRoot(), "src", "StarterApp.Functions", "host.json");
        using var document = JsonDocument.Parse(File.ReadAllText(hostJsonPath));

        Assert.True(document.RootElement.TryGetProperty("retry", out var retry),
            "host.json must define a host-level retry policy; without one, abandoned messages burn MaxDeliveryCount in seconds.");

        Assert.Equal("exponentialBackoff", retry.GetProperty("strategy").GetString());

        var maxRetryCount = retry.GetProperty("maxRetryCount").GetInt32();
        Assert.InRange(maxRetryCount, 3, 10);

        var minimumInterval = TimeSpan.Parse(retry.GetProperty("minimumInterval").GetString()!, CultureInfo.InvariantCulture);
        var maximumInterval = TimeSpan.Parse(retry.GetProperty("maximumInterval").GetString()!, CultureInfo.InvariantCulture);
        Assert.True(minimumInterval >= TimeSpan.FromSeconds(1), "Retry minimum interval must back off, not hot-loop.");
        Assert.True(maximumInterval >= minimumInterval, "Retry maximum interval must not undercut the minimum.");

        var lockRenewal = TimeSpan.Parse(
            document.RootElement.GetProperty("extensions").GetProperty("serviceBus").GetProperty("maxAutoLockRenewalDuration").GetString()!,
            CultureInfo.InvariantCulture);

        // Worst case is bounded by maxRetryCount * maximumInterval; it must fit inside the lock
        // renewal window or retries silently race lock loss and the message redelivers mid-retry.
        // Require it to stay within 80% of the window rather than exactly on the ceiling: the linear
        // bound ignores per-attempt handler execution time and renewal jitter, so the remaining 20%
        // is deliberate headroom. A change that erases the margin (e.g. maximumInterval back to 60s)
        // fails here instead of shipping a zero-margin config.
        var worstCaseRetryWindow = maximumInterval * maxRetryCount;
        Assert.True(worstCaseRetryWindow <= lockRenewal * 0.8,
            $"Retry worst case ({worstCaseRetryWindow}) must stay within 80% of maxAutoLockRenewalDuration " +
            $"({lockRenewal}); leave headroom for per-attempt handler execution, not just bare fit.");
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
