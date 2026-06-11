using DbUp.Helpers;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class ReportingQueryTests
{
    private readonly ApiTestFixture _fixture;

    public ReportingQueryTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    // Every operational reporting query must execute against the current schema, so a
    // column rename breaks this test at PR time instead of breaking an operator
    // mid-incident. SELECT-only by convention (scripts/reporting/README.md).
    [Fact]
    public void EveryReportingQuery_ExecutesAgainstTheCurrentSchema()
    {
        var failures = new List<string>();

        foreach (var path in ReportingQueryFiles())
        {
            var sql = System.IO.File.ReadAllText(path);
            var upgradeEngine = DeployChanges.To
                .PostgresqlDatabase(_fixture.ConnectionString)
                .WithScript(System.IO.Path.GetFileName(path), sql)
                .JournalTo(new NullJournal())
                .Build();

            var result = upgradeEngine.PerformUpgrade();
            if (!result.Successful)
                failures.Add($"{System.IO.Path.GetFileName(path)}: {result.Error?.Message}");
        }

        Assert.True(failures.Count == 0,
            "Reporting queries no longer match the schema — fix the query (or the rename) before merging:\n" +
            string.Join("\n", failures));
    }

    private static IEnumerable<string> ReportingQueryFiles()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "StarterApp.slnx")))
            {
                var reporting = System.IO.Path.Combine(directory.FullName, "scripts", "reporting");
                var files = System.IO.Directory.GetFiles(reporting, "*.sql");
                Assert.NotEmpty(files);
                return files;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
    }
}
