namespace StarterApp.Tests.Consistency;

/// <summary>
/// Governance checks for the command-handler exemplar set. Concrete binding of
/// <see cref="CohortGovernanceTestBase{TFingerprint}"/> — the shared [Fact]s run
/// against the command-handler cohort when this class is discovered.
/// </summary>
public class CommandHandlerGovernanceTests : CohortGovernanceTestBase<HandlerFingerprint>
{
    protected override ICohortDefinition<HandlerFingerprint> Cohort { get; } = new CommandHandlerCohort();
    protected override string ExemplarDocsFolder => "command-handlers";
    protected override string SourceTreeRelativePath => Path.Combine("src", "StarterApp.Api", "Application");
    protected override string SourceFileGlob => "*Command.cs";
    protected override string SourceFileNameToTypeName(string fileNameWithoutExtension) =>
        fileNameWithoutExtension + "Handler";
}
