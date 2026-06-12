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
    // Read-only is enforced, not aspirational: the session runs with
    // default_transaction_read_only=on, so a mutating statement in a support query fails
    // here the same way it would against a sanely-configured operator session.
    private string ReadOnlyConnectionString =>
        _fixture.ConnectionString + ";Options=-c default_transaction_read_only=on";

    [Fact]
    public void EveryReportingQuery_ExecutesAgainstTheCurrentSchema()
    {
        var failures = new List<string>();

        foreach (var path in ReportingQueryFiles())
        {
            var sql = System.IO.File.ReadAllText(path);
            var upgradeEngine = DeployChanges.To
                .PostgresqlDatabase(ReadOnlyConnectionString)
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

    [Fact]
    public void ReadOnlyGuard_RejectsMutatingStatements()
    {
        var upgradeEngine = DeployChanges.To
            .PostgresqlDatabase(ReadOnlyConnectionString)
            .WithScript("mutating-probe.sql", "DELETE FROM outbox_messages")
            .JournalTo(new NullJournal())
            .Build();

        var result = upgradeEngine.PerformUpgrade();

        Assert.False(result.Successful, "the read-only session guard must reject mutating SQL");
    }

    [Fact]
    public void KnowledgeBaseVerificationQueries_ExecuteAgainstTheCurrentSchema()
    {
        // The incident knowledge base's verification queries are the evidence step operators
        // run BEFORE acting; schema drift must break them at PR time, same as the reporting pack.
        var failures = new List<string>();

        foreach (var path in KnowledgeBaseFiles())
        {
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            foreach (var (id, query) in EnumerateQueries(document))
            {
                if (!query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                    continue;

                var upgradeEngine = DeployChanges.To
                    .PostgresqlDatabase(ReadOnlyConnectionString)
                    .WithScript($"{System.IO.Path.GetFileName(path)}:{id}", query)
                    .JournalTo(new NullJournal())
                    .Build();

                var result = upgradeEngine.PerformUpgrade();
                if (!result.Successful)
                    failures.Add($"{System.IO.Path.GetFileName(path)} '{id}': {result.Error?.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "Knowledge-base verification queries no longer match the schema:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<(string Id, string Query)> EnumerateQueries(JsonDocument document)
    {
        var root = document.RootElement;

        if (root.TryGetProperty("knownPatterns", out var patterns) && patterns.ValueKind == JsonValueKind.Array)
        {
            foreach (var pattern in patterns.EnumerateArray())
            {
                if (pattern.TryGetProperty("verification", out var verification) &&
                    verification.ValueKind == JsonValueKind.Object &&
                    verification.TryGetProperty("query", out var query) &&
                    query.ValueKind == JsonValueKind.String)
                {
                    yield return (pattern.TryGetProperty("id", out var id) ? id.GetString() ?? "?" : "?", query.GetString()!);
                }
            }
        }

        if (root.TryGetProperty("verificationTemplates", out var templates) && templates.ValueKind == JsonValueKind.Array)
        {
            foreach (var template in templates.EnumerateArray())
            {
                if (template.TryGetProperty("query", out var query) && query.ValueKind == JsonValueKind.String)
                    yield return (template.TryGetProperty("id", out var id) ? id.GetString() ?? "?" : "?", query.GetString()!);
            }
        }
    }

    private static IEnumerable<string> KnowledgeBaseFiles()
    {
        var investigations = System.IO.Path.Combine(RepoRoot(), "docs", "investigations");
        return System.IO.Directory.Exists(investigations)
            ? System.IO.Directory.EnumerateFiles(investigations, "knowledge-base.json", System.IO.SearchOption.AllDirectories)
            : [];
    }

    private static string RepoRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "StarterApp.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
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
