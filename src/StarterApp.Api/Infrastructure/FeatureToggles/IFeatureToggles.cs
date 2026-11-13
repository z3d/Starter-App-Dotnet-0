namespace StarterApp.Api.Infrastructure.FeatureToggles;

public interface IFeatureToggles
{
    bool IsEnabled(string name);
}
