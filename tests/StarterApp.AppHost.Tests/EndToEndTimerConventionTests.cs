using System.Reflection;
using StarterApp.Functions;

namespace StarterApp.AppHost.Tests;

public class EndToEndTimerConventionTests
{
    // A timer added to the worker and missed from the AppHost's list would fire in the middle of an end-to-end run.
    [Fact]
    public void EveryTimerFunction_IsSwitchedOffInTheEndToEndRun()
    {
        var appHost = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "dev", "StarterApp.AppHost", "Program.cs"));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        var timers = typeof(OrderConfirmationEmailFunction).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(flags))
            .Where(method => method.GetParameters().Any(parameter =>
                parameter.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "TimerTriggerAttribute")))
            .Select(method => method.GetCustomAttributes().First(attribute => attribute.GetType().Name == "FunctionAttribute"))
            .Select(attribute => (string)attribute.GetType().GetProperty("Name")!.GetValue(attribute)!)
            .ToList();

        Assert.NotEmpty(timers);
        var missing = timers.Where(name => !appHost.Contains($"\"{name}\"", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0,
            "Add these timer functions to the EndToEnd:TimersOff list in dev/StarterApp.AppHost/Program.cs: " + string.Join(", ", missing));
        Assert.DoesNotContain(timers, name => name.Contains('-', StringComparison.Ordinal));
    }
}
