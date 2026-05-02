using Microsoft.EntityFrameworkCore;

namespace StarterApp.Tests.Consistency;

/// <summary>
/// Cohort definition for EF Core entity configurations — any non-abstract class implementing
/// <see cref="IEntityTypeConfiguration{TEntity}"/>. Features count occurrences of key
/// fluent-API methods (<c>OwnsOne</c>, <c>HasIndex</c>, <c>Property</c>, <c>HasConversion</c>,
/// <c>HasMany</c>) in the configuration's IL.
/// </summary>
public class EfConfigurationCohort : ICohortDefinition<EfConfigurationFingerprint>
{
    private static readonly Assembly ApiAssembly = typeof(StarterApp.Api.Infrastructure.IApiMarker).Assembly;

    public string CohortName => "EfConfigurations";

    /// <summary>
    /// Must match docs/exemplars/ef-configurations/README.md.
    /// </summary>
    public IReadOnlyList<string> ExemplarTypeNames =>
    [
        "ProductConfiguration",
        "OrderConfiguration",
        "OutboxMessageConfiguration"
    ];

    public IReadOnlyList<Type> DiscoverTypes()
    {
        return ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Configuration"))
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)))
            .OrderBy(t => t.Name)
            .ToList();
    }

    public EfConfigurationFingerprint Extract(Type configType)
    {
        var configureMethod = configType.GetMethod(
            "Configure",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<>)
                .MakeGenericType(ResolveEntityType(configType))],
            modifiers: null) ?? throw new InvalidOperationException(
                $"Could not locate Configure method on {configType.Name}");

        return new EfConfigurationFingerprint
        {
            TypeName = configType.Name,
            IlByteSize = IlInspector.SumIlByteSize(configType),
            OwnsOneCount = IlInspector.CountMethodCallsByName(configureMethod, "OwnsOne"),
            HasIndexCount = IlInspector.CountMethodCallsByName(configureMethod, "HasIndex"),
            PropertyConfigCount = IlInspector.CountMethodCallsByName(configureMethod, "Property"),
            HasConversionCount = IlInspector.CountMethodCallsByName(configureMethod, "HasConversion"),
            HasManyCount = IlInspector.CountMethodCallsByName(configureMethod, "HasMany")
        };
    }

    private static Type ResolveEntityType(Type configType)
    {
        var iface = configType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>));

        return iface.GetGenericArguments()[0];
    }
}
