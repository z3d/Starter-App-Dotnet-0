namespace StarterApp.Tests.Consistency;

/// <summary>
/// Governance checks for the query-handler exemplar set. Concrete binding of
/// <see cref="CohortGovernanceTestBase{TFingerprint}"/>.
/// </summary>
public class QueryHandlerGovernanceTests : CohortGovernanceTestBase<QueryHandlerFingerprint>
{
    protected override ICohortDefinition<QueryHandlerFingerprint> Cohort { get; } = new QueryHandlerCohort();
    protected override string ExemplarDocsFolder => "query-handlers";
    protected override string SourceTreeRelativePath => Path.Combine("src", "StarterApp.Api", "Application");
    protected override string SourceFileGlob => "*Query.cs";
    protected override string SourceFileNameToTypeName(string fileNameWithoutExtension) =>
        fileNameWithoutExtension + "Handler";
}
