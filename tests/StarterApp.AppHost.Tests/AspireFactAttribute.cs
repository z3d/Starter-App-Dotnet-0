namespace StarterApp.AppHost.Tests;

// Opt-in gate for the facts that boot the whole distributed app. Unless STARTERAPP_ASPIRE_TESTS
// is "true" the fact is skipped statically, so a plain `dotnet test` reports it as skipped rather
// than spending minutes on containers it has no runtime for. A skip is visible in the run summary;
// an early `return` inside the test body would count as a pass. AspireE2EFixture checks the same
// flag, because xUnit constructs the collection fixture even when every fact in it is skipped.
[AttributeUsage(AttributeTargets.Method)]
public sealed class AspireFactAttribute : FactAttribute
{
    public const string OptInVariable = "STARTERAPP_ASPIRE_TESTS";

    public static bool Enabled { get; } =
        bool.TryParse(Environment.GetEnvironmentVariable(OptInVariable), out var enabled) && enabled;

    public AspireFactAttribute()
    {
        if (!Enabled)
            Skip = $"Aspire end-to-end tests are opt-in: set {OptInVariable}=true to run them.";
    }
}
