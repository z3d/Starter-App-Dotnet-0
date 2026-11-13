namespace StarterApp.Api.Infrastructure.FeatureToggles;

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
        return _configuration.GetValue($"{FeatureToggleOptions.SectionName}:{name}", defaultValue: false);
    }
}
