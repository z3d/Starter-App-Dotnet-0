namespace StarterApp.Tests.Conventions;

// Mechanical guardrails for docs/investigations/**/knowledge-base.json (see the README there).
// The load-bearing rule: a knowledge base must never become a place where known defects quietly
// age — every defect entry needs a fix commit or an explicit accepted-limitation reference.
public class KnowledgeBaseConventionTests
{
    [Fact]
    public void KnowledgeBases_MustParseAndCarryRequiredFields()
    {
        var failures = new List<string>();

        foreach (var path in KnowledgeBaseFiles())
        {
            var name = RelativePath(path);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                failures.Add($"{name}: not valid JSON — {ex.Message}");
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                foreach (var section in new[] { "knownPatterns", "knownDefects", "verificationTemplates", "investigationHistory" })
                {
                    if (!root.TryGetProperty(section, out var value) || value.ValueKind != JsonValueKind.Array)
                        failures.Add($"{name}: missing required array '{section}'");
                }

                if (root.TryGetProperty("knownPatterns", out var patterns) && patterns.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pattern in patterns.EnumerateArray())
                    {
                        var id = StringField(pattern, "id") ?? "<missing id>";
                        if (StringField(pattern, "defaultAction") is null)
                            failures.Add($"{name}: pattern '{id}' has no defaultAction");
                        if (!pattern.TryGetProperty("verification", out var verification) ||
                            verification.ValueKind != JsonValueKind.Object ||
                            StringField(verification, "query") is null)
                        {
                            failures.Add($"{name}: pattern '{id}' has no verification.query — a default action without " +
                                         "a verification query invites acting on an unconfirmed hypothesis");
                        }
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, "Knowledge-base files must be valid and complete:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void KnownDefects_MustLinkAFixCommitOrAcceptedLimitation()
    {
        var failures = new List<string>();

        foreach (var path in KnowledgeBaseFiles())
        {
            var name = RelativePath(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("knownDefects", out var defects) || defects.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var defect in defects.EnumerateArray())
            {
                var id = StringField(defect, "id") ?? "<missing id>";
                var hasFix = !string.IsNullOrWhiteSpace(StringField(defect, "fixCommit"));
                var hasAccepted = !string.IsNullOrWhiteSpace(StringField(defect, "acceptedLimitationRef"));

                if (!hasFix && !hasAccepted)
                {
                    failures.Add($"{name}: defect '{id}' has neither fixCommit nor acceptedLimitationRef. " +
                                 "Fix it (and record the commit) or make the accepted-limitation decision in " +
                                 "docs/ARCHITECTURE_REVIEW.md and reference it — defects must not quietly age here.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static string? StringField(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static IEnumerable<string> KnowledgeBaseFiles()
    {
        var investigations = Path.Combine(RepoRoot(), "docs", "investigations");
        return Directory.Exists(investigations)
            ? Directory.EnumerateFiles(investigations, "knowledge-base.json", SearchOption.AllDirectories)
            : [];
    }

    private static string RelativePath(string path) => Path.GetRelativePath(RepoRoot(), path);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StarterApp.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
    }
}
