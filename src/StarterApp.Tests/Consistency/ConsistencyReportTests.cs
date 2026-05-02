namespace StarterApp.Tests.Consistency;

public class ConsistencyReportTests
{
    private readonly ITestOutputHelper _output;

    public ConsistencyReportTests(ITestOutputHelper output) => _output = output;

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
    public void EveryReportCohort_ProducesAllThreeMeasurementLayers()
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
        WriteShingleScores(report, memberLabel, memberNameWidth);
        WriteEmbeddingScores(report, memberLabel, memberNameWidth);
        WriteFeatureDivergences(report, memberNameWidth);
        WriteSummary(report);

        AssertCompleteReportHealth(report);
    }

    private void WriteStructuralScores(
        CohortConsistencyReport report,
        string memberLabel,
        int memberNameWidth,
        string structuralHeader,
        Func<CohortScore, string> formatStructuralRow)
    {
        _output.WriteLine($"=== {report.CohortName} STRUCTURAL SCORES ===");
        _output.WriteLine($"{Pad(memberLabel, memberNameWidth)} {structuralHeader}");
        _output.WriteLine(new string('-', memberNameWidth + structuralHeader.Length + 1));

        foreach (var score in report.StructuralScores)
            _output.WriteLine($"{Pad(score.TypeName, memberNameWidth)} {formatStructuralRow(score)}");
    }

    private void WriteShingleScores(CohortConsistencyReport report, string memberLabel, int memberNameWidth)
    {
        _output.WriteLine($"\n=== {report.CohortName} AST SHINGLE SCORES ===");
        _output.WriteLine($"{Pad(memberLabel, memberNameWidth)} {"AvgSim",8} {"Shingles",9}");
        _output.WriteLine(new string('-', memberNameWidth + 19));

        foreach (var score in report.ShingleScores)
            _output.WriteLine($"{Pad(score.TypeName, memberNameWidth)} {score.AverageSimilarity,8:F3} {score.ShingleCount,9}");
    }

    private void WriteEmbeddingScores(CohortConsistencyReport report, string memberLabel, int memberNameWidth)
    {
        _output.WriteLine($"\n=== {report.CohortName} EMBEDDING SIMILARITY SCORES (source-token embedder) ===");
        _output.WriteLine($"{Pad(memberLabel, memberNameWidth)} {"CosSim",8}");
        _output.WriteLine(new string('-', memberNameWidth + 9));

        foreach (var score in report.EmbeddingScores)
            _output.WriteLine($"{Pad(score.TypeName, memberNameWidth)} {score.CosineSimilarity,8:F3}");
    }

    private void WriteFeatureDivergences(CohortConsistencyReport report, int memberNameWidth)
    {
        _output.WriteLine($"\n=== {report.CohortName} PER-FEATURE DIVERGENCE REPORT ===");

        foreach (var divergence in report.FeatureDivergences)
        {
            if (!divergence.HasDivergence)
            {
                _output.WriteLine($"\n{divergence.FeatureName}: no divergence");
                continue;
            }

            var exemplarLabel = FormatExemplarLabel(divergence);

            _output.WriteLine($"\n{divergence.FeatureName} - Exemplar: {exemplarLabel} - {divergence.DivergentCount} divergent:");
            foreach (var member in divergence.DivergentMembers)
                _output.WriteLine($"  {Pad(member.TypeName, memberNameWidth)} actual={member.ActualValue:F1}  exemplar={member.ExemplarValue:F1}");
        }
    }

    private void WriteSummary(CohortConsistencyReport report)
    {
        var avgDist = report.StructuralScores.Average(s => s.Distance);
        var stdDev = Math.Sqrt(report.StructuralScores.Average(s => (s.Distance - avgDist) * (s.Distance - avgDist)));
        var structuralOutliers = report.StructuralScores.Where(s => s.Distance > avgDist + 2 * stdDev).ToList();

        var avgShingle = report.ShingleScores.Average(s => s.AverageSimilarity);
        var minShingle = report.ShingleScores.First();

        var avgEmbedding = report.EmbeddingScores.Average(s => s.CosineSimilarity);
        var minEmbedding = report.EmbeddingScores.First();

        _output.WriteLine($"\n=== {report.CohortName} SUMMARY ===");
        _output.WriteLine($"Total members: {report.Fingerprints.Count}");
        _output.WriteLine($"Exemplars: {string.Join(", ", report.Exemplars.Select(e => e.TypeName))}");
        _output.WriteLine($"Structural - Mean distance: {avgDist:F2}, StdDev: {stdDev:F2}, 2-sigma: {avgDist + 2 * stdDev:F2}");
        _output.WriteLine(
            $"Structural outliers (>2-sigma): {(structuralOutliers.Count > 0 ? string.Join(", ", structuralOutliers.Select(s => s.TypeName)) : "(none)")}");
        _output.WriteLine($"Shingles - Mean similarity: {avgShingle:F3}, Min: {minShingle.AverageSimilarity:F3} ({minShingle.TypeName})");
        _output.WriteLine($"Embedding - Mean similarity: {avgEmbedding:F3}, Min: {minEmbedding.CosineSimilarity:F3} ({minEmbedding.TypeName})");
    }

    private static void AssertCompleteReportHealth(CohortConsistencyReport report)
    {
        Assert.NotEmpty(report.MemberTypes);
        Assert.NotEmpty(report.Fingerprints);
        Assert.NotEmpty(report.Exemplars);
        Assert.NotEmpty(report.FeatureDivergences);

        Assert.Equal(report.MemberTypes.Count, report.Fingerprints.Count);
        Assert.Equal(report.MemberTypes.Count, report.StructuralScores.Count);
        Assert.Equal(report.MemberTypes.Count, report.ShingleScores.Count);
        Assert.Equal(report.MemberTypes.Count, report.EmbeddingScores.Count);
        Assert.Equal(report.Fingerprints[0].FeatureNames.Length, report.FeatureDivergences.Count);

        Assert.All(report.StructuralScores, s => Assert.True(double.IsFinite(s.Distance),
            $"{report.CohortName}.{s.TypeName} structural distance is not finite: {s.Distance}"));
        Assert.All(report.ShingleScores, s => Assert.True(double.IsFinite(s.AverageSimilarity),
            $"{report.CohortName}.{s.TypeName} shingle similarity is not finite: {s.AverageSimilarity}"));
        Assert.All(report.EmbeddingScores, s => Assert.True(double.IsFinite(s.CosineSimilarity),
            $"{report.CohortName}.{s.TypeName} cosine similarity is not finite: {s.CosineSimilarity}"));

        for (var i = 1; i < report.StructuralScores.Count; i++)
            Assert.True(report.StructuralScores[i - 1].Distance >= report.StructuralScores[i].Distance,
                $"{report.CohortName} structural scores not in descending order at index {i}");

        for (var i = 1; i < report.ShingleScores.Count; i++)
            Assert.True(report.ShingleScores[i - 1].AverageSimilarity <= report.ShingleScores[i].AverageSimilarity,
                $"{report.CohortName} shingle scores not in ascending similarity order at index {i}");

        for (var i = 1; i < report.EmbeddingScores.Count; i++)
            Assert.True(report.EmbeddingScores[i - 1].CosineSimilarity <= report.EmbeddingScores[i].CosineSimilarity,
                $"{report.CohortName} embedding scores not in ascending similarity order at index {i}");

        var discoveredNames = report.MemberTypes.Select(t => t.Name).ToHashSet();
        Assert.Equal(discoveredNames, report.Fingerprints.Select(f => f.TypeName).ToHashSet());
        Assert.Equal(discoveredNames, report.StructuralScores.Select(s => s.TypeName).ToHashSet());
        Assert.Equal(discoveredNames, report.ShingleScores.Select(s => s.TypeName).ToHashSet());
        Assert.Equal(discoveredNames, report.EmbeddingScores.Select(s => s.TypeName).ToHashSet());
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
