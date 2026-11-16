namespace StarterApp.Tests.Conventions;

// A worker's files sit in folders named after the module they serve, cross-module scheduled jobs in Housekeeping and the
// inbox, settlement and identity plumbing in Infrastructure; one project, one namespace (docs/DECISIONS.md, "The worker's
// files sit in folders named after what they serve").
public class WorkerLayoutConventionTests
{
    private static readonly string WorkerRoot = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.Functions");

    private static IEnumerable<string> WorkerSourceFiles() =>
        Directory.EnumerateFiles(WorkerRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    [Fact]
    public void OnlyProgram_SitsAtTheWorkersRoot()
    {
        var loose = WorkerSourceFiles()
            .Where(path => Path.GetDirectoryName(path) == WorkerRoot && Path.GetFileName(path) != "Program.cs")
            .Select(path => Path.GetRelativePath(TestPaths.RepoRoot, path))
            .ToList();

        Assert.True(loose.Count == 0, "Put each worker file in the folder of the module it serves, Housekeeping or Infrastructure:\n" + string.Join("\n", loose));
    }

    [Fact]
    public void EveryWorkerFile_KeepsTheProjectsOneNamespace()
    {
        var failures = WorkerSourceFiles()
            .Where(path => File.ReadLines(path).Any(line => line.StartsWith("namespace ", StringComparison.Ordinal) && line.Trim() != "namespace StarterApp.Functions;"))
            .Select(path => Path.GetRelativePath(TestPaths.RepoRoot, path))
            .ToList();

        Assert.True(failures.Count == 0, "A worker folder is not a namespace; keep namespace StarterApp.Functions:\n" + string.Join("\n", failures));
    }
}
