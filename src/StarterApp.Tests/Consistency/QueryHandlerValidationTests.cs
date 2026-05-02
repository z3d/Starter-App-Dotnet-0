using System.Text.RegularExpressions;

namespace StarterApp.Tests.Consistency;

public class QueryHandlerValidationTests : CohortValidationTestBase<QueryHandlerFingerprint>
{
    private readonly QueryHandlerCohort _cohort = new();

    protected override ICohortDefinition<QueryHandlerFingerprint> Cohort => _cohort;
    protected override string ExemplarDocsFolder => "query-handlers";
    protected override string SourceTreeRelativePath => Path.Combine("src", "StarterApp.Api", "Application");
    protected override string SourceFileGlob => "*Query.cs";
    protected override string ExemplarNameSuffix => "QueryHandler";
    protected override string SourceFileNameToTypeName(string fileNameWithoutExtension) =>
        fileNameWithoutExtension + "Handler";

    protected override void AssertFingerprintIsValid(QueryHandlerFingerprint fp)
    {
        Assert.True(fp.IlByteSize > 0, $"{fp.TypeName} has zero IL byte size");
        Assert.True(fp.ConstructorDependencyCount >= 1,
            $"{fp.TypeName} has no constructor dependencies (should at least have IDbConnection)");
    }

    [Fact]
    public void StructuralFingerprint_CapturesGetOrderByIdRichReadShape()
    {
        var fingerprints = _cohort.DiscoverTypes().Select(_cohort.Extract).ToList();
        var target = fingerprints.FirstOrDefault(f => f.TypeName == "GetOrderByIdQueryHandler");

        Assert.NotNull(target);
        Assert.False(target.ReturnsList);
        Assert.False(target.HasPagination);
        Assert.True(target.SqlStatementCount >= 2,
            "GetOrderByIdQueryHandler should expose its root-plus-items multi-statement read shape.");
        Assert.True(target.JoinCount > 0,
            "GetOrderByIdQueryHandler should expose its aggregate projection SQL shape.");
    }

    [Fact]
    public void ExemplarAlignment_DocumentedDependencyCountsMatchCode()
    {
        var readmeText = File.ReadAllText(ExemplarReadmePath);
        var pattern = $@"\*\*`(\w+{ExemplarNameSuffix})\.cs`\*\*.*?(\d+)\s+dependenc";
        var matches = Regex.Matches(readmeText, pattern);

        Assert.True(matches.Count > 0,
            $"Could not parse any dependency counts from {ExemplarReadmePath}.");

        var fingerprints = _cohort.DiscoverTypes().Select(_cohort.Extract).ToDictionary(f => f.TypeName);
        var violations = new List<string>();

        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var documented = int.Parse(match.Groups[2].Value);
            if (!fingerprints.TryGetValue(name, out var fp))
            {
                violations.Add($"{name}: not found in extracted fingerprints");
                continue;
            }
            if (fp.ConstructorDependencyCount != documented)
                violations.Add($"{name}: README says {documented} dependencies, code has {fp.ConstructorDependencyCount}");
        }

        Assert.True(violations.Count == 0, $"Exemplar README drift:\n{string.Join("\n", violations)}");
    }
}
