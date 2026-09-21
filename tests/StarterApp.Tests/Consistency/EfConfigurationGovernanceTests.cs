namespace StarterApp.Tests.Consistency;

/// <summary>
/// Governance checks for the EF-configuration exemplar set. Concrete binding of
/// <see cref="CohortGovernanceTestBase{TFingerprint}"/>.
/// </summary>
public class EfConfigurationGovernanceTests : CohortGovernanceTestBase<EfConfigurationFingerprint>
{
    protected override ICohortDefinition<EfConfigurationFingerprint> Cohort { get; } = new EfConfigurationCohort();
    protected override string ExemplarDocsFolder => "ef-configurations";
    protected override string SourceTreeRelativePath => Path.Combine("src", "StarterApp.Api", "Data", "Configurations");
    protected override string SourceFileGlob => "*Configuration.cs";
}
