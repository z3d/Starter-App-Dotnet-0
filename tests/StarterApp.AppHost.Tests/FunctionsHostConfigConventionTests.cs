using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StarterApp.AppHost.Tests;

public partial class FunctionsHostConfigConventionTests
{
    [Fact]
    public void HandlerRetryDeadline_MustFitInsideLockRenewal_WithoutUnsupportedHostPolicy()
    {
        var hostJsonPath = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.Functions", "host.json");
        using var document = JsonDocument.Parse(File.ReadAllText(hostJsonPath));
        Assert.False(document.RootElement.TryGetProperty("retry", out _),
            "Service Bus does not support Functions execution-retry policies; retry handler work explicitly.");
        var lockRenewal = TimeSpan.Parse(
            document.RootElement.GetProperty("extensions").GetProperty("serviceBus").GetProperty("maxAutoLockRenewalDuration").GetString()!,
            CultureInfo.InvariantCulture);

        // The deadline bounds handler work and backoff; settlement runs afterwards on the host
        // token and needs its own reserve of lock time. Both bounds carry real margin: the hard
        // bound is strict, and the deadline alone stays within 80% of the renewal window so
        // per-attempt overrun and renewal jitter have somewhere to go.
        var deadline = StarterApp.Functions.MessageSettlement.ExecutionTimeout;
        var reserve = StarterApp.Functions.MessageSettlement.SettlementReserve;
        Assert.True(deadline + reserve < lockRenewal,
            $"ExecutionTimeout ({deadline}) plus SettlementReserve ({reserve}) must be strictly inside maxAutoLockRenewalDuration ({lockRenewal}).");
        Assert.True(deadline <= lockRenewal * 0.8,
            $"ExecutionTimeout ({deadline}) must stay within 80% of maxAutoLockRenewalDuration ({lockRenewal}).");
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

    // A %Section:Key% timer schedule with no value fails that function's indexing on the real host
    // (see above), so every schedule setting carries a default wherever the worker runs: baked into
    // the image, in local.settings.json for the Core Tools, and set by the AppHost on the container.
    [Fact]
    public void EveryTimerSchedule_IsASettingWithADefaultEverywhereTheWorkerRuns()
    {
        var functionsDirectory = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.Functions");
        var settings = Directory.EnumerateFiles(functionsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => TimerSettingRegex().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(settings);

        var dockerfile = File.ReadAllText(Path.Combine(functionsDirectory, "Dockerfile"));
        var localSettings = File.ReadAllText(Path.Combine(functionsDirectory, "local.settings.json"));
        var appHost = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.AppHost", "Program.cs"));

        var failures = new List<string>();
        foreach (var setting in settings)
        {
            var variable = setting.Replace(':', '_').Replace("_", "__", StringComparison.Ordinal);
            if (!dockerfile.Contains($"ENV {variable}=", StringComparison.Ordinal))
                failures.Add($"{setting}: the Functions image bakes no default (ENV {variable}=...)");
            if (!localSettings.Contains($"\"{variable}\"", StringComparison.Ordinal))
                failures.Add($"{setting}: local.settings.json does not set {variable}");
            if (!appHost.Contains($"\"{variable}\"", StringComparison.Ordinal))
                failures.Add($"{setting}: the AppHost does not set {variable} on the functions container");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [GeneratedRegex("""TimerTrigger\("%([^%"]+)%"\)""")]
    private static partial Regex TimerSettingRegex();
}
