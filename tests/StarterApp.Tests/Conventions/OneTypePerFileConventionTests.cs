namespace StarterApp.Tests.Conventions;

// One type per file. Two things may share: a command or query with its handler, and a DTO or read model with the small
// records only it uses (docs/DECISIONS.md, "One type per file"). IdentityValidators.cs held twenty-eight classes before this.
public partial class OneTypePerFileConventionTests
{
    [GeneratedRegex(@"^(?:public|internal)\s+(?:(?:sealed|static|abstract|partial|readonly|unsafe)\s+)*(?:record struct|record|class|interface|enum|struct)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex TypeDeclaration();

    [Fact]
    public void EachSourceFile_HoldsOneType_OrAnAllowedPair()
    {
        var failures = Violations(SourceFiles().Select(path => (Path.GetRelativePath(TestPaths.RepoRoot, path), File.ReadAllText(path))));

        Assert.True(failures.Count == 0, "One type per file (a command or query keeps its handler; a DTO or read model keeps the records only it uses):\n" + string.Join("\n", failures));
    }

    [Fact]
    public void TheCheck_AllowsThePairs_AndSeesEverythingElse()
    {
        const string commandAndHandler = "public record OpenCaseCommand(Guid Id);\npublic class OpenCaseCommandHandler\n{\n}\n";
        const string dtoAndItsLine = "public record LeaseDto(Guid Id, IReadOnlyList<LeaseLineDto> Lines);\npublic record LeaseLineDto(decimal Amount);\n";
        const string twoDtos = "public record LeaseDto(Guid Id);\npublic record QuoteDto(Guid Id);\n";
        const string twoValidators = "public class OpenCaseValidator\n{\n}\npublic class CloseCaseValidator\n{\n}\n";

        Assert.Empty(Violations([("OpenCaseCommand.cs", commandAndHandler)]));
        Assert.Empty(Violations([("LeaseDto.cs", dtoAndItsLine)]));
        Assert.Single(Violations([("LeaseDto.cs", twoDtos)]));
        Assert.Single(Violations([("LeaseLineDto.cs", dtoAndItsLine)]));
        Assert.Single(Violations([("CaseValidators.cs", twoValidators)]));
    }

    private static List<string> Violations(IEnumerable<(string Path, string Text)> files)
    {
        var failures = new List<string>();
        foreach (var (path, text) in files)
        {
            var types = TypeDeclaration().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
            var requests = types.Where(t => t.EndsWith("Command", StringComparison.Ordinal) || t.EndsWith("Query", StringComparison.Ordinal)).ToList();
            types.RemoveAll(t => t.EndsWith("Handler", StringComparison.Ordinal) && requests.Contains(t[..^"Handler".Length]));
            if (types.Count < 2)
                continue;

            var owner = Path.GetFileNameWithoutExtension(path);
            var ownerHoldsData = owner.EndsWith("Dto", StringComparison.Ordinal) || owner.EndsWith("ReadModel", StringComparison.Ordinal);
            var strangers = types.Where(t => t != owner && !(ownerHoldsData && types.Contains(owner) && IsUsedByAnotherTypeHere(t, text))).ToList();
            if (strangers.Count > 0)
                failures.Add($"{path} also declares {string.Join(", ", strangers)}");
        }

        return failures;
    }

    // Named at least once besides its own declaration, so something else in the file is built from it.
    private static bool IsUsedByAnotherTypeHere(string type, string text) =>
        Regex.Count(text, $@"\b{Regex.Escape(type)}\b") > 1;

    private static IEnumerable<string> SourceFiles() =>
        new[] { "src", "dev" }.SelectMany(root => Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, root), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
