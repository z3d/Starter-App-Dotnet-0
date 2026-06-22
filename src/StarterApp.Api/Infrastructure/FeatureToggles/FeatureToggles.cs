namespace StarterApp.Api.Infrastructure.FeatureToggles;

public interface IFeatureToggles
{
    bool IsEnabled(string name);
}

// Configuration-driven (FeatureToggles:{name}); any provider works, so a toggle can be
// flipped via appsettings, environment variable, or orchestration config without a
// redeploy. A missing entry means ENABLED — toggles exist to switch features off, and
// the convention test requires every declared toggle to have an explicit entry anyway.
public sealed class ConfigurationFeatureToggles : IFeatureToggles
{
    private readonly IConfiguration _configuration;

    public ConfigurationFeatureToggles(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsEnabled(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _configuration.GetValue($"FeatureToggles:{name}", defaultValue: true);
    }
}

public sealed class FeatureDisabledException : Exception
{
    public FeatureDisabledException(string featureName)
        : base($"The feature '{featureName}' is currently disabled.")
    {
        FeatureName = featureName;
    }

    public string FeatureName { get; }
}
