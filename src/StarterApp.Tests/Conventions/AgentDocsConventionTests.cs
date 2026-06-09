namespace StarterApp.Tests.Conventions;

// CLAUDE.md/.claude/skills and AGENTS.md/.agents/skills are maintained as mirrors for different
// agent harnesses. The only permitted drift is the agent-specific doc name and skills path
// (CLAUDE.md <-> AGENTS.md, .claude/skills <-> .agents/skills). Canonicalizing those four tokens
// to self/other placeholders must make every mirrored file pair identical; any other difference
// is unintended drift. A genuinely harness-specific difference beyond the token swap requires
// extending this canonicalization with a documented reason.
public class AgentDocsConventionTests : ConventionTestBase
{
    private const string ClaudeDoc = "CLAUDE.md";
    private const string AgentsDoc = "AGENTS.md";
    private const string ClaudeSkillsDir = ".claude/skills";
    private const string AgentsSkillsDir = ".agents/skills";

    [Fact]
    public void AgentSkillTrees_MustContainIdenticalFileSets()
    {
        var root = FindRepoRoot();
        var claudeFiles = EnumerateSkillFiles(Path.Combine(root, ClaudeSkillsDir));
        var agentsFiles = EnumerateSkillFiles(Path.Combine(root, AgentsSkillsDir));

        var failures = claudeFiles.Except(agentsFiles)
            .Select(file => $"{AgentsSkillsDir}/{file} is missing (exists under {ClaudeSkillsDir})")
            .Concat(agentsFiles.Except(claudeFiles)
                .Select(file => $"{ClaudeSkillsDir}/{file} is missing (exists under {AgentsSkillsDir})"))
            .ToList();

        Assert.True(failures.Count == 0,
            $"{ClaudeSkillsDir} and {AgentsSkillsDir} must mirror the same file set; add or remove skill files in both trees:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void MirroredAgentDocs_MustMatchAfterAgentSpecificTokenSwap()
    {
        var root = FindRepoRoot();
        var failures = new List<string>();

        CompareMirroredPair(root, ClaudeDoc, AgentsDoc, failures);

        var shared = EnumerateSkillFiles(Path.Combine(root, ClaudeSkillsDir))
            .Intersect(EnumerateSkillFiles(Path.Combine(root, AgentsSkillsDir)));
        foreach (var relative in shared)
            CompareMirroredPair(root, $"{ClaudeSkillsDir}/{relative}", $"{AgentsSkillsDir}/{relative}", failures);

        Assert.True(failures.Count == 0,
            "Mirrored agent docs drifted beyond the permitted agent-specific token swap " +
            $"({ClaudeDoc} <-> {AgentsDoc}, {ClaudeSkillsDir} <-> {AgentsSkillsDir}). " +
            "Apply the same edit to both files, adapting only those tokens (each file leads with its own doc/skills names):\n" +
            string.Join("\n", failures));
    }

    private static void CompareMirroredPair(string root, string claudeRelative, string agentsRelative, List<string> failures)
    {
        var claudePath = Path.Combine(root, claudeRelative);
        var agentsPath = Path.Combine(root, agentsRelative);
        Assert.True(File.Exists(claudePath), $"Expected '{claudeRelative}' to exist at the repo root.");
        Assert.True(File.Exists(agentsPath), $"Expected '{agentsRelative}' to exist at the repo root.");

        var claudeLines = CanonicalLines(claudePath, isClaudeSide: true);
        var agentsLines = CanonicalLines(agentsPath, isClaudeSide: false);

        if (claudeLines.SequenceEqual(agentsLines, StringComparer.Ordinal))
            return;

        var line = 0;
        while (line < claudeLines.Length && line < agentsLines.Length &&
               string.Equals(claudeLines[line], agentsLines[line], StringComparison.Ordinal))
            line++;

        var claudeLine = line < claudeLines.Length ? claudeLines[line] : "<end of file>";
        var agentsLine = line < agentsLines.Length ? agentsLines[line] : "<end of file>";
        failures.Add($"{claudeRelative}:{line + 1} and {agentsRelative}:{line + 1} differ after token canonicalization:\n" +
                     $"  {claudeRelative}: {claudeLine}\n" +
                     $"  {agentsRelative}: {agentsLine}");
    }

    private static string[] CanonicalLines(string path, bool isClaudeSide)
    {
        var (selfDoc, selfSkills, otherDoc, otherSkills) = isClaudeSide
            ? (ClaudeDoc, ClaudeSkillsDir, AgentsDoc, AgentsSkillsDir)
            : (AgentsDoc, AgentsSkillsDir, ClaudeDoc, ClaudeSkillsDir);

        return File.ReadAllText(path)
            .Replace(selfSkills, "{SELF-SKILLS}", StringComparison.Ordinal)
            .Replace(otherSkills, "{OTHER-SKILLS}", StringComparison.Ordinal)
            .Replace(selfDoc, "{SELF-DOC}", StringComparison.Ordinal)
            .Replace(otherDoc, "{OTHER-DOC}", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }

    private static SortedSet<string> EnumerateSkillFiles(string skillsRoot)
    {
        Assert.True(Directory.Exists(skillsRoot), $"Expected skills directory '{skillsRoot}' to exist.");

        var files = Directory.EnumerateFiles(skillsRoot, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).StartsWith('.'))
            .Select(file => Path.GetRelativePath(skillsRoot, file).Replace(Path.DirectorySeparatorChar, '/'));
        return new SortedSet<string>(files, StringComparer.Ordinal);
    }

    private static string FindRepoRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "StarterApp.slnx")) ||
                    File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
