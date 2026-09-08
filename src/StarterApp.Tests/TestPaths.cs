namespace StarterApp.Tests;

// The one repo-root resolver for tests that read source, config, or docs from disk. Walks up from
// the test binary, then the working directory, to the solution file. Deterministic builds set
// PathMap, so [CallerFilePath] cannot anchor this at compile time.
public static class TestPaths
{
    public static string RepoRoot { get; } = Find();

    private static string Find()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "StarterApp.slnx")))
                    return dir.FullName;

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
    }
}
