namespace StarterApp.Functions;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WatchedJobAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
