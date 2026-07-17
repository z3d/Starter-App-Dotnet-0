namespace StarterApp.AppHost.Tests;

public class AspireCollectionTraitConventionTests
{
    private const string AspireCollectionName = "Aspire E2E";
    private const string CategoryTraitName = "Category";
    private const string AspireTraitValue = "Aspire";

    // The CI unit job filters out Aspire facts with `Category!=Aspire`. That filter is only sound if
    // every test class joining the "Aspire E2E" collection also carries [Trait("Category","Aspire")]:
    // a collection member without the trait (and without "Integration" in its name) would boot the
    // full distributed rig inside the unit job, where nothing is provisioned for it, and hang or fail.
    // The trait is the mechanical contract the CI filter depends on — pin it so a future Aspire test
    // cannot silently opt out of the exclusion.
    [Fact]
    public void EveryAspireCollectionMember_MustCarryTheAspireCategoryTrait()
    {
        var offenders = typeof(AspireE2EFixture).Assembly.GetTypes()
            .Where(JoinsAspireCollection)
            .Where(type => !HasAspireCategoryTrait(type))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Every class in the \"{AspireCollectionName}\" collection must also declare " +
            $"[Trait(\"{CategoryTraitName}\", \"{AspireTraitValue}\")] so the CI unit job's " +
            "`Category!=Aspire` filter excludes it. Missing on:\n" + string.Join("\n", offenders));
    }

    private static bool JoinsAspireCollection(Type type) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.Name == "CollectionAttribute" &&
            attribute.ConstructorArguments.Count == 1 &&
            attribute.ConstructorArguments[0].Value as string == AspireCollectionName);

    private static bool HasAspireCategoryTrait(Type type) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.Name == "TraitAttribute" &&
            attribute.ConstructorArguments.Count == 2 &&
            attribute.ConstructorArguments[0].Value as string == CategoryTraitName &&
            attribute.ConstructorArguments[1].Value as string == AspireTraitValue);
}
