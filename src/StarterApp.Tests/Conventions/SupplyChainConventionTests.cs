namespace StarterApp.Tests.Conventions;

public class SupplyChainConventionTests : ConventionTestBase
{
    [Fact]
    public void Dockerfiles_MustPinBaseImagesByDigest()
    {
        var failures = new List<string>();

        foreach (var dockerfile in EnumerateDockerfiles())
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(dockerfile))
            {
                lineNumber++;
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var imageRef = ParseFromImageReference(trimmed);
                if (imageRef is null || IsBuildStageReference(imageRef))
                    continue;

                if (!imageRef.Contains("@sha256:", StringComparison.Ordinal))
                    failures.Add($"{FormatPath(dockerfile)}:{lineNumber} base image '{imageRef}' is pinned by a mutable tag. " +
                                 "Pin it by immutable digest (tag@sha256:...) so a repointed tag cannot silently change the build. " +
                                 "Resolve with: docker buildx imagetools inspect <image>.");
            }
        }

        Assert.True(failures.Count == 0,
            "Docker base images must be pinned by immutable @sha256 digest (supply-chain finding M1):\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void NuGetConfig_MustRestrictSourcesAndMapAllPackagesToNuGetOrg()
    {
        var configPath = Path.Combine(FindRepoRoot(), "NuGet.config");
        Assert.True(File.Exists(configPath),
            "A repo-root NuGet.config must exist to constrain package feeds (supply-chain finding L1).");

        var document = XDocument.Load(configPath);

        var packageSources = document.Root?.Elements("packageSources").SingleOrDefault();
        Assert.True(packageSources is not null, "NuGet.config must declare a <packageSources> section.");

        Assert.True(packageSources!.Elements("clear").Any(),
            "<packageSources> must <clear/> inherited sources so a machine/user feed cannot inject a dependency-confusion path.");

        var sourceAdds = packageSources.Elements("add").ToList();
        Assert.True(sourceAdds.Count == 1,
            "Exactly one package source (nuget.org) must be declared; additional feeds reopen dependency-confusion risk.");
        Assert.Contains("api.nuget.org", (string?)sourceAdds[0].Attribute("value") ?? string.Empty, StringComparison.Ordinal);

        var mapping = document.Root?.Elements("packageSourceMapping").SingleOrDefault();
        Assert.True(mapping is not null,
            "<packageSourceMapping> must bind package ids to nuget.org (primary dependency-confusion defense).");

        var wildcardMapped = mapping!.Elements("packageSource")
            .Any(ps => ps.Elements("package").Any(p => (string?)p.Attribute("pattern") == "*"));
        Assert.True(wildcardMapped,
            "packageSourceMapping must map the '*' pattern so every package id resolves only from the declared source.");
    }

    [Fact]
    public void GlobalJson_MustPinSdkVersion()
    {
        var globalJsonPath = Path.Combine(FindRepoRoot(), "global.json");
        Assert.True(File.Exists(globalJsonPath),
            "A repo-root global.json must pin the SDK band so the build toolchain is reproducible.");

        var content = File.ReadAllText(globalJsonPath);
        Assert.Contains("\"sdk\"", content, StringComparison.Ordinal);
        Assert.Contains("\"version\"", content, StringComparison.Ordinal);
        Assert.Contains("\"rollForward\"", content, StringComparison.Ordinal);
    }

    private static string? ParseFromImageReference(string fromLine)
    {
        var tokens = fromLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        // tokens[0] == "FROM"; skip option flags like --platform=...; the image ref is the next bare token.
        for (var i = 1; i < tokens.Length; i++)
        {
            if (tokens[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            return tokens[i];
        }

        return null;
    }

    // A FROM may reference an earlier build stage by alias (e.g. `FROM build AS final`); such a
    // reference is a bare name with no registry path/tag and must not be digest-pinned.
    private static bool IsBuildStageReference(string imageRef)
    {
        return !imageRef.Contains('/', StringComparison.Ordinal) &&
               !imageRef.Contains(':', StringComparison.Ordinal) &&
               !imageRef.Contains('.', StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateDockerfiles()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        return Directory.EnumerateFiles(srcRoot, "Dockerfile", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"));
    }

    private static string FormatPath(string file)
    {
        return Path.GetRelativePath(FindRepoRoot(), file);
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
