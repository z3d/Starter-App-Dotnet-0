namespace StarterApp.Tests.Consistency;

public class ConsistencyReportTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _reportLines = [];

    public ConsistencyReportTests(ITestOutputHelper output) => _output = output;

    private void WriteLine(string line)
    {
        _output.WriteLine(line);
        _reportLines.Add(line);
    }

    // The report is advisory; its value is being READ. Alongside the test console it lands in
    // docs/_local/ (git-ignored) so it can be opened without re-running the suite — see the
    // testing-strategy skill for how to consume it.
    private void EmitReportFile(string cohortName)
    {
        var directory = System.IO.Path.Combine(FindRepoRoot(), "docs", "_local");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllLines(
            System.IO.Path.Combine(directory, $"consistency-{cohortName.ToLowerInvariant().Replace(' ', '-')}.txt"),
            _reportLines);
        _reportLines.Clear();
    }

    private static string FindRepoRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "StarterApp.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root from the test base directory.");
    }

    [Fact]
    public void GenerateFullReport()
    {
        GenerateReport(
            new CommandHandlerCohort(),
            "Handler",
            50,
            $"{"Dist",7} {"ILSize",7} {"Deps",5} {"Log",4} {"Cache",6} {"Try",4} {"Priv",5} {"Loads",6} {"TopContrib",-25}",
            FormatCommandHandlerRow);
    }

    [Fact]
    public void GenerateQueryHandlerReport()
    {
        GenerateReport(
            new QueryHandlerCohort(),
            "Handler",
            56,
            $"{"Dist",7} {"ILSize",7} {"Deps",5} {"Paged",6} {"Cache",6} {"List",5} {"Joins",6} {"SQL",5} {"TopContrib",-28}",
            FormatQueryHandlerRow);
    }

    [Fact]
    public void GenerateEfConfigurationReport()
    {
        GenerateReport(
            new EfConfigurationCohort(),
            "Configuration",
            52,
            $"{"Dist",7} {"ILSize",7} {"OwnsOne",8} {"Index",6} {"Prop",5} {"Conv",5} {"Many",5} {"TopContrib",-22}",
            FormatEfConfigurationRow);
    }

    [Fact]
    public void EveryReportCohort_ProducesStructuralAndDivergenceLayers()
    {
        var reports = new[]
        {
            CohortConsistencyReporter.Build(new CommandHandlerCohort()),
            CohortConsistencyReporter.Build(new QueryHandlerCohort()),
            CohortConsistencyReporter.Build(new EfConfigurationCohort())
        };

        foreach (var report in reports)
            AssertCompleteReportHealth(report);
    }

    private void GenerateReport<TFingerprint>(
        ICohortDefinition<TFingerprint> cohort,
        string memberLabel,
        int memberNameWidth,
        string structuralHeader,
        Func<CohortScore, string> formatStructuralRow)
        where TFingerprint : ICohortFingerprint
    {
        var report = CohortConsistencyReporter.Build(cohort);

        WriteStructuralScores(report, memberLabel, memberNameWidth, structuralHeader, formatStructuralRow);
        WriteFeatureDivergences(report, memberNameWidth);
        WriteSummary(report);
        EmitReportFile(report.CohortName);

        AssertCompleteReportHealth(report);
    }

    private void WriteStructuralScores(
        CohortConsistencyReport report,
        string memberLabel,
        int memberNameWidth,
        string structuralHeader,
        Func<CohortScore, string> formatStructuralRow)
    {
        WriteLine($"=== {report.CohortName} STRUCTURAL SCORES ===");
        WriteLine($"{Pad(memberLabel, memberNameWidth)} {structuralHeader}");
        WriteLine(new string('-', memberNameWidth + structuralHeader.Length + 1));

        foreach (var score in report.StructuralScores)
            WriteLine($"{Pad(score.TypeName, memberNameWidth)} {formatStructuralRow(score)}");
    }



    private void WriteFeatureDivergences(CohortConsistencyReport report, int memberNameWidth)
    {
        WriteLine($"\n=== {report.CohortName} PER-FEATURE DIVERGENCE REPORT ===");

        foreach (var divergence in report.FeatureDivergences)
        {
            if (!divergence.HasDivergence)
            {
                WriteLine($"\n{divergence.FeatureName}: no divergence");
                continue;
            }

            var exemplarLabel = FormatExemplarLabel(divergence);

            WriteLine($"\n{divergence.FeatureName} - Exemplar: {exemplarLabel} - {divergence.DivergentCount} divergent:");
            foreach (var member in divergence.DivergentMembers)
                WriteLine($"  {Pad(member.TypeName, memberNameWidth)} actual={member.ActualValue:F1}  exemplar={member.ExemplarValue:F1}");
        }
    }

    private void WriteSummary(CohortConsistencyReport report)
    {
        var avgDist = report.StructuralScores.Average(s => s.Distance);
        var stdDev = Math.Sqrt(report.StructuralScores.Average(s => (s.Distance - avgDist) * (s.Distance - avgDist)));
        var structuralOutliers = report.StructuralScores.Where(s => s.Distance > avgDist + 2 * stdDev).ToList();

        WriteLine($"\n=== {report.CohortName} SUMMARY ===");
        WriteLine($"Total members: {report.Fingerprints.Count}");
        WriteLine($"Exemplars: {string.Join(", ", report.Exemplars.Select(e => e.TypeName))}");
        WriteLine($"Structural - Mean distance: {avgDist:F2}, StdDev: {stdDev:F2}, 2-sigma: {avgDist + 2 * stdDev:F2}");
        WriteLine(
            $"Structural outliers (>2-sigma): {(structuralOutliers.Count > 0 ? string.Join(", ", structuralOutliers.Select(s => s.TypeName)) : "(none)")}");
    }

    private static void AssertCompleteReportHealth(CohortConsistencyReport report)
    {
        Assert.NotEmpty(report.MemberTypes);
        Assert.NotEmpty(report.Fingerprints);
        Assert.NotEmpty(report.Exemplars);
        Assert.NotEmpty(report.FeatureDivergences);

        Assert.Equal(report.MemberTypes.Count, report.Fingerprints.Count);
        Assert.Equal(report.MemberTypes.Count, report.StructuralScores.Count);
        Assert.Equal(report.Fingerprints[0].FeatureNames.Length, report.FeatureDivergences.Count);

        Assert.All(report.StructuralScores, s => Assert.True(double.IsFinite(s.Distance),
            $"{report.CohortName}.{s.TypeName} structural distance is not finite: {s.Distance}"));
        for (var i = 1; i < report.StructuralScores.Count; i++)
            Assert.True(report.StructuralScores[i - 1].Distance >= report.StructuralScores[i].Distance,
                $"{report.CohortName} structural scores not in descending order at index {i}");


        var discoveredNames = report.MemberTypes.Select(t => t.Name).ToHashSet();
        Assert.Equal(discoveredNames, report.Fingerprints.Select(f => f.TypeName).ToHashSet());
        Assert.Equal(discoveredNames, report.StructuralScores.Select(s => s.TypeName).ToHashSet());
    }

    private static string FormatCommandHandlerRow(CohortScore score)
    {
        var fingerprint = (HandlerFingerprint)score.Fingerprint;
        return $"{score.Distance,7:F2} {fingerprint.IlByteSize,7} {fingerprint.ConstructorDependencyCount,5} " +
            $"{(fingerprint.HasLogger ? "Y" : "N"),4} {(fingerprint.HasCacheInvalidator ? "Y" : "N"),6} " +
            $"{(fingerprint.HasTryCatch ? "Y" : "N"),4} {fingerprint.PrivateMethodCount,5} {fingerprint.EntityLoadCount,6} {score.TopContributor,-25}";
    }

    private static string FormatQueryHandlerRow(CohortScore score)
    {
        var fingerprint = (QueryHandlerFingerprint)score.Fingerprint;
        return $"{score.Distance,7:F2} {fingerprint.IlByteSize,7} {fingerprint.ConstructorDependencyCount,5} " +
            $"{(fingerprint.HasPagination ? "Y" : "N"),6} {(fingerprint.IsCacheable ? "Y" : "N"),6} " +
            $"{(fingerprint.ReturnsList ? "Y" : "N"),5} {fingerprint.JoinCount,6} {fingerprint.SqlStatementCount,5} {score.TopContributor,-28}";
    }

    private static string FormatEfConfigurationRow(CohortScore score)
    {
        var fingerprint = (EfConfigurationFingerprint)score.Fingerprint;
        return $"{score.Distance,7:F2} {fingerprint.IlByteSize,7} {fingerprint.OwnsOneCount,8} {fingerprint.HasIndexCount,6} " +
            $"{fingerprint.PropertyConfigCount,5} {fingerprint.HasConversionCount,5} {fingerprint.HasManyCount,5} {score.TopContributor,-22}";
    }

    private static string FormatExemplarLabel(FeatureDivergence divergence)
    {
        if (!divergence.IsBoolean)
            return $"mean={divergence.ExemplarValue:F1}, stddev={divergence.ExemplarStdDev:F1}";

        return divergence.ExemplarStdDev > 1e-10
            ? "mixed"
            : divergence.ExemplarValue >= 0.5 ? "all true" : "all false";
    }

    private static string Pad(string value, int width) =>
        value.Length >= width ? value : value.PadRight(width);
}
