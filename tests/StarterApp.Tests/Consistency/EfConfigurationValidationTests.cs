namespace StarterApp.Tests.Consistency;

public class EfConfigurationValidationTests : CohortValidationTestBase<EfConfigurationFingerprint>
{
    private readonly EfConfigurationCohort _cohort = new();

    protected override ICohortDefinition<EfConfigurationFingerprint> Cohort => _cohort;
    protected override string ExemplarDocsFolder => "ef-configurations";
    protected override string SourceTreeRelativePath => Path.Combine("src", "StarterApp.Api", "Data", "Configurations");
    protected override string SourceFileGlob => "*Configuration.cs";
    protected override string ExemplarNameSuffix => "Configuration";

    protected override void AssertFingerprintIsValid(EfConfigurationFingerprint fp)
    {
        Assert.True(fp.IlByteSize > 0, $"{fp.TypeName} has zero IL byte size");
        Assert.True(fp.PropertyConfigCount > 0,
            $"{fp.TypeName} has zero Property configurations — every entity should at least map its primary key scalar");
    }

    [Fact]
    public void StructuralFingerprint_CapturesOrderAggregateMappingShape()
    {
        var fingerprints = _cohort.DiscoverTypes().Select(_cohort.Extract).ToList();
        var orderFingerprint = fingerprints.First(f => f.TypeName == "OrderConfiguration");

        Assert.True(orderFingerprint.HasManyCount > 0, "OrderConfiguration should be detected as the child-collection mapping.");
        Assert.True(orderFingerprint.HasConversionCount > 0, "OrderConfiguration should be detected as the enum-conversion mapping.");
    }
}
