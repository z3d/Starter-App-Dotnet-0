namespace StarterApp.Api.Infrastructure.FeatureToggles;

public sealed class FeatureDisabledException : Exception
{
    public FeatureDisabledException(string featureName)
        : base($"The feature '{featureName}' is currently disabled.")
    {
        FeatureName = featureName;
    }

    public string FeatureName { get; }
}
