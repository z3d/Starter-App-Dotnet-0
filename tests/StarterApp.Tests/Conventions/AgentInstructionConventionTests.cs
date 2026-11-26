namespace StarterApp.Tests.Conventions;

public class AgentInstructionConventionTests : ConventionTestBase
{
    private const int RootBudgetBytes = 24_000;

    [Fact]
    public void RootInstructions_MustStayWithinTheAlwaysLoadedBudget()
    {
        var failures = new[] { "AGENTS.md", "CLAUDE.md" }
            .Select(name => Path.Combine(TestPaths.RepoRoot, name))
            .Where(File.Exists)
            .Select(file => (file, bytes: new FileInfo(file).Length))
            .Where(entry => entry.bytes > RootBudgetBytes)
            .Select(entry => $"{Path.GetFileName(entry.file)} is {entry.bytes} bytes")
            .ToList();

        Assert.True(failures.Count == 0,
            $"The root instruction file loads into every session and every subagent, so it stays under {RootBudgetBytes} bytes. " +
            "Move what matters in one area to a path-scoped file in .claude/rules/, and a procedure to a skill " +
            "(.claude/skills/skill-authoring). Over budget: " + string.Join(", ", failures));
    }

    [Fact]
    public void Rules_MustBeScopedToPaths()
    {
        var rulesRoot = Path.Combine(TestPaths.RepoRoot, ".claude", "rules");
        var unscoped = Directory.EnumerateFiles(rulesRoot, "*.md", SearchOption.AllDirectories)
            .Where(file => !HasPathsFrontmatter(File.ReadAllLines(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(unscoped.Count == 0,
            "A rule file without `paths:` frontmatter loads at launch like the root file and escapes its budget. " +
            "Unscoped: " + string.Join(", ", unscoped));
    }

    [Fact]
    public void RootInstructions_MustNotImportOtherFiles()
    {
        var agents = Path.Combine(TestPaths.RepoRoot, "AGENTS.md");
        var imports = File.ReadAllLines(agents)
            .Where(line => line.StartsWith('@'))
            .ToList();

        Assert.True(imports.Count == 0,
            "An @import loads with the file that imports it, so it costs the same as pasting the file in. " +
            "Name the file in backticks instead. Imports: " + string.Join(", ", imports));
    }

    private static bool HasPathsFrontmatter(string[] lines)
    {
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return false;

        return lines.Skip(1)
            .TakeWhile(line => line.Trim() != "---")
            .Any(line => line.StartsWith("paths:", StringComparison.Ordinal));
    }
}
