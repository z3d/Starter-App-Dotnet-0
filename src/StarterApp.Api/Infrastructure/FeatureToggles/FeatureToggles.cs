namespace StarterApp.Api.Infrastructure.FeatureToggles;

public interface IFeatureToggles
{
    bool IsEnabled(string name);
}

// A missing entry means enabled; the convention test requires an explicit entry per toggle anyway.
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
