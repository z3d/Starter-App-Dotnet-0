namespace StarterApp.AppHost.Tests;

public class AspireCollectionConventionTests
{
    private const string AspireCollectionName = "Aspire E2E";

    // A plain [Fact] on a class in the Aspire collection would construct the fixture and boot the
    // distributed app in every run, including the unit run and any machine without a container
    // runtime. [AspireFact] is what keeps those runs safe, so every fact in the collection must be one.
    [Fact]
    public void EveryFactInTheAspireCollection_MustBeAnAspireFact()
    {
        var offenders = typeof(AspireE2EFixture).Assembly.GetTypes()
            .Where(JoinsAspireCollection)
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length != 0
                          && method.GetCustomAttributes(typeof(AspireFactAttribute), inherit: false).Length == 0)
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Every test in the \"{AspireCollectionName}\" collection must use [AspireFact] so it stays " +
            $"skipped unless {AspireFactAttribute.OptInVariable}=true. Found:\n" + string.Join("\n", offenders));
    }

    private static bool JoinsAspireCollection(Type type) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.Name == "CollectionAttribute" &&
            attribute.ConstructorArguments.Count == 1 &&
            attribute.ConstructorArguments[0].Value as string == AspireCollectionName);
}
