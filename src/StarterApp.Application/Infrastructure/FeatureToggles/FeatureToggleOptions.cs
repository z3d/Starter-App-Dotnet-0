namespace StarterApp.Application.Infrastructure.FeatureToggles;

public sealed class FeatureToggleOptions
{
    public const string SectionName = "FeatureToggles";

    public IReadOnlyDictionary<string, string?> Entries { get; set; } = new Dictionary<string, string?>();
}
