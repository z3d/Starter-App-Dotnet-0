namespace StarterApp.Tests.Conventions;

// One command or query per file, named after it, with its handler beside it. Grouped files (LeaseCommands.cs held fifteen)
// came in with the first module and every later module copied them; no rule said otherwise (docs/DECISIONS.md, "One command or query per file").
public partial class CommandFileConventionTests
{
    [GeneratedRegex(@"^(?:public|internal) (?:sealed )?(?:record|class) (\w+(?:Command|Query))\b", RegexOptions.Multiline)]
    private static partial Regex RequestDeclaration();

    [GeneratedRegex(@"^(?:public|internal) (?:sealed )?class (\w+(?:Command|Query))Handler\b", RegexOptions.Multiline)]
    private static partial Regex HandlerDeclaration();

    [Fact]
    public void EachCommandAndQuery_HasItsOwnFile_NamedAfterIt_WithItsHandler()
    {
        var failures = Violations(SourceFiles().Select(path => (path, File.ReadAllText(path))));

        Assert.True(failures.Count == 0, "One command or query per file, named after it, with its handler in the same file:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void TheCheck_SeesAGroupedFile_AMisnamedFile_AndAStrayHandler()
    {
        const string two = "public record OpenCaseCommand(Guid Id) : ICommand;\npublic record CloseCaseCommand(Guid Id) : ICommand;\n";
        const string one = "public record OpenCaseCommand(Guid Id) : ICommand;\npublic class OpenCaseCommandHandler\n{\n}\n";
        const string stray = "public class OpenCaseCommandHandler\n{\n}\n";

        Assert.Single(Violations([("CaseCommands.cs", two)]));
        Assert.Single(Violations([("CaseCommands.cs", one)]));
        Assert.Single(Violations([("Handlers.cs", stray)]));
        Assert.Empty(Violations([("OpenCaseCommand.cs", one)]));
    }

    private static List<string> Violations(IEnumerable<(string Path, string Text)> files)
    {
        var failures = new List<string>();
        foreach (var (path, text) in files)
        {
            var requests = RequestDeclaration().Matches(text).Select(m => m.Groups[1].Value).ToList();
            var handled = HandlerDeclaration().Matches(text).Select(m => m.Groups[1].Value).ToList();
            var file = Path.GetFileNameWithoutExtension(path);

            if (requests.Count > 1)
                failures.Add($"{path} declares {requests.Count}: {string.Join(", ", requests)}");
            else if (requests.Count == 1 && requests[0] != file)
                failures.Add($"{path} declares {requests[0]}; name the file {requests[0]}.cs");

            foreach (var stray in handled.Where(h => !requests.Contains(h)))
                failures.Add($"{path} holds {stray}Handler without {stray}");
        }

        return failures;
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
