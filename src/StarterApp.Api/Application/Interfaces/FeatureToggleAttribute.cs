namespace StarterApp.Api.Application.Interfaces;

// If the FeatureToggles:{Name} setting is off, the mediator answers 503 before any validator or behaviour runs. A disabled feature is never served from the cache.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FeatureToggleAttribute : Attribute
{
    public FeatureToggleAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}
