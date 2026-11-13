using Microsoft.Extensions.Options;

namespace StarterApp.Api.Infrastructure.FeatureToggles;

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
        var stray = options.Entries.Keys.Where(key => !_declared.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (stray.Count > 0)
        {
            var known = _declared.Count == 0 ? "none" : string.Join(", ", _declared);
            return ValidateOptionsResult.Fail(
                $"{FeatureToggleOptions.SectionName} names {string.Join(", ", stray)}, which no [FeatureToggle] declares. Known toggles: {known}.");
        }

        var unreadable = options.Entries.Where(entry => !bool.TryParse(entry.Value, out _)).Select(entry => entry.Key).ToList();
        if (unreadable.Count > 0)
            return ValidateOptionsResult.Fail(
                $"{FeatureToggleOptions.SectionName} gives {string.Join(", ", unreadable)} a value that is not true or false.");

        return ValidateOptionsResult.Success;
    }
}
