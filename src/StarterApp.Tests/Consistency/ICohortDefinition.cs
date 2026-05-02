namespace StarterApp.Tests.Consistency;

/// <summary>
/// Defines a cohort: how to discover its members, extract fingerprints, and which members are exemplars.
/// Implement per cohort (command handlers, query handlers, domain entities, etc.).
/// </summary>
public interface ICohortDefinition<out TFingerprint> where TFingerprint : ICohortFingerprint
{
    string CohortName { get; }
    IReadOnlyList<Type> DiscoverTypes();
    TFingerprint Extract(Type type);
    IReadOnlyList<string> ExemplarTypeNames { get; }
}
