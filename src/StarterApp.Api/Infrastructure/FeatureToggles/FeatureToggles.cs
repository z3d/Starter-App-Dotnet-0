using Microsoft.Extensions.Options;

namespace StarterApp.Api.Infrastructure.FeatureToggles;

public interface IFeatureToggles
{
    bool IsEnabled(string name);
}

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

public sealed class FeatureToggleOptions
{
    public const string SectionName = "FeatureToggles";

    public IReadOnlyList<string> ConfiguredKeys { get; set; } = [];
}

public sealed class DeclaredFeatureToggles : IValidateOptions<FeatureToggleOptions>
{
    private readonly IReadOnlyList<string> _declared;

    private DeclaredFeatureToggles(IReadOnlyList<string> declared)
    {
        _declared = declared;
    }

    public static DeclaredFeatureToggles In(IEnumerable<Assembly> assemblies) =>
        new(assemblies.SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false)?.Name)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList());

    public ValidateOptionsResult Validate(string? name, FeatureToggleOptions options)
    {
        var stray = options.ConfiguredKeys.Where(key => !_declared.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (stray.Count == 0)
            return ValidateOptionsResult.Success;

        var known = _declared.Count == 0 ? "none" : string.Join(", ", _declared);
        return ValidateOptionsResult.Fail(
            $"{FeatureToggleOptions.SectionName} names {string.Join(", ", stray)}, which no [FeatureToggle] declares. Known toggles: {known}.");
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
