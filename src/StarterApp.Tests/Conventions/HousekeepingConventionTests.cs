namespace StarterApp.Tests.Conventions;

public class HousekeepingConventionTests : ConventionTestBase
{
    private static readonly Regex GlobalUsingDirectiveRegex = new(@"^global\s+using\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*);$", RegexOptions.Compiled);
    private static readonly Regex SimpleUsingDirectiveRegex = new(@"^using\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*);$", RegexOptions.Compiled);

    [Fact]
    public void ProjectFiles_MustNotReferenceBinOrObjArtifacts()
    {
        var failures = new List<string>();

        foreach (var file in EnumerateProjectFiles())
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var attribute in document.Descendants().Attributes())
            {
                if (!ReferencesBinOrObjArtifact(attribute.Value))
                    continue;

                var lineInfo = (IXmlLineInfo)attribute;
                failures.Add($"{FormatPath(file)}:{lineInfo.LineNumber} {attribute.Name} references '{attribute.Value}'. Use project/package references instead of build-output artifacts.");
            }

            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "HintPath"))
            {
                if (!ReferencesBinOrObjArtifact(element.Value))
                    continue;

                var lineInfo = (IXmlLineInfo)element;
                failures.Add($"{FormatPath(file)}:{lineInfo.LineNumber} HintPath references '{element.Value}'. Use project/package references instead of build-output artifacts.");
            }
        }

        Assert.True(failures.Count == 0,
            "Project files must not reference DLLs or generated files from bin/obj directories:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void ProductionCode_MustNotUseRegionsXmlDocsOrHistoricalComments()
    {
        var failures = new List<string>();

        foreach (var file in EnumerateProductionSourceFiles())
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("///", StringComparison.Ordinal))
                    failures.Add($"{FormatPath(file)}:{lineNumber} XML documentation comments are not used in app code; prefer clear names or a short // why-comment.");

                if (Regex.IsMatch(trimmed, @"^#\s*(region|endregion)\b", RegexOptions.IgnoreCase))
                    failures.Add($"{FormatPath(file)}:{lineNumber} regions hide structure; split or simplify the type instead.");

                if (Regex.IsMatch(trimmed, @"^//.*\b(HACK|TEMPORARY|WORKAROUND|LEGACY|REMOVE LATER)\b", RegexOptions.IgnoreCase))
                    failures.Add($"{FormatPath(file)}:{lineNumber} historical/workaround comments must be resolved or captured in issue/docs context.");
            }
        }

        Assert.True(failures.Count == 0,
            "Production code comment hygiene violations:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ProductionLogs_MustNotWriteRawBodyPlaceholders()
    {
        var failures = new List<string>();

        foreach (var file in EnumerateProductionSourceFiles())
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (line.Contains("{Body}", StringComparison.Ordinal))
                    failures.Add($"{FormatPath(file)}:{lineNumber} raw Body placeholders must not be written to logs; archive full payloads and log redacted payloads only.");
            }
        }

        Assert.True(failures.Count == 0,
            "Production logs must not write raw body placeholders:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void SourceFiles_MustNotRepeatProjectGlobalUsings()
    {
        var failures = new List<string>();

        foreach (var projectDirectory in EnumerateProjectDirectories())
        {
            var globalUsingsPath = Path.Combine(projectDirectory, "GlobalUsings.cs");
            if (!File.Exists(globalUsingsPath))
                continue;

            var globalNamespaces = ReadGlobalUsingNamespaces(globalUsingsPath)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(file => !IsInIgnoredDirectory(file))
                         .Where(file => !Path.GetFileName(file).Equals("GlobalUsings.cs", StringComparison.Ordinal)))
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (!TryParseSimpleUsingDirective(line, out var usingNamespace))
                        continue;

                    if (globalNamespaces.Contains(usingNamespace))
                        failures.Add($"{FormatPath(file)}:{lineNumber} '{usingNamespace}' is already in {FormatPath(globalUsingsPath)}. Remove the local using directive.");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Source files must rely on project GlobalUsings.cs for globally imported namespaces:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void ConventionTestFiles_MustUseGlobalUsings()
    {
        var conventionRoot = Path.Combine(FindRepoRoot(), "src", "StarterApp.Tests", "Conventions");
        var failures = new List<string>();

        foreach (var file in Directory.EnumerateFiles(conventionRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !IsInIgnoredDirectory(file)))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (IsUsingDirective(line))
                    failures.Add($"{FormatPath(file)}:{lineNumber} move this using directive to src/StarterApp.Tests/GlobalUsings.cs.");
            }
        }

        Assert.True(failures.Count == 0,
            "Convention tests share one import surface. Add convention-test imports to GlobalUsings.cs instead of individual files:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void AppHost_MustRunFunctionsWithAzureFunctionsRuntimeContainer()
    {
        var root = FindRepoRoot();
        var appHostProgram = File.ReadAllText(Path.Combine(root, "src", "StarterApp.AppHost", "Program.cs"));
        var functionsDockerfile = File.ReadAllText(Path.Combine(root, "src", "StarterApp.Functions", "Dockerfile"));

        Assert.Contains("AddDockerfile(\"functions\"", appHostProgram);
        Assert.Contains("IResourceWithAzureFunctionsConfig", appHostProgram);
        Assert.Contains("AzureWebJobsStorage", appHostProgram);
        Assert.Contains("servicebus", appHostProgram);
        Assert.Contains("ConnectionStrings__payloadarchive", appHostProgram);
        Assert.Contains("FROM --platform=linux/amd64 mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0", functionsDockerfile);
        Assert.Contains("dotnet publish src/StarterApp.Functions/StarterApp.Functions.csproj -c Release -o /app/publish", functionsDockerfile);
        Assert.DoesNotContain("--no-restore", functionsDockerfile);
    }

    [Fact]
    public void AppHostSdkVersion_MustMatchAspirePackageVersion()
    {
        var root = FindRepoRoot();
        var centralPackages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var expectedVersion = centralPackages.Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .Single(element => (string?)element.Attribute("Include") == "Aspire.Hosting.AppHost")
            .Attribute("Version")?.Value;

        var appHostProject = XDocument.Load(Path.Combine(root, "src", "StarterApp.AppHost", "StarterApp.AppHost.csproj"));
        var actualVersion = appHostProject.Root?.Elements()
            .Single(element => element.Name.LocalName == "Sdk" && (string?)element.Attribute("Name") == "Aspire.AppHost.Sdk")
            .Attribute("Version")?.Value;

        Assert.Equal(expectedVersion, actualVersion);
    }

    private static IEnumerable<string> EnumerateProjectDirectories()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        foreach (var projectFile in Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories)
                     .Where(file => !IsInIgnoredDirectory(file)))
        {
            var projectDirectory = Path.GetDirectoryName(projectFile);
            if (projectDirectory != null)
                yield return projectDirectory;
        }
    }

    private static IEnumerable<string> EnumerateProjectFiles()
    {
        var root = FindRepoRoot();
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(file => !IsInIgnoredDirectory(file));
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles()
    {
        return Directory.EnumerateFiles(Path.Combine(FindRepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsInIgnoredDirectory(file))
            .Where(file => !file.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static IEnumerable<string> ReadGlobalUsingNamespaces(string globalUsingsPath)
    {
        foreach (var line in File.ReadLines(globalUsingsPath))
        {
            var match = GlobalUsingDirectiveRegex.Match(line.TrimStart('\uFEFF'));
            if (match.Success)
                yield return match.Groups["namespace"].Value;
        }
    }

    private static bool TryParseSimpleUsingDirective(string line, out string usingNamespace)
    {
        usingNamespace = string.Empty;
        var match = SimpleUsingDirectiveRegex.Match(line);
        if (!match.Success)
            return false;

        usingNamespace = match.Groups["namespace"].Value;
        return true;
    }

    private static bool IsUsingDirective(string line)
    {
        return line.StartsWith("using ", StringComparison.Ordinal) &&
               !line.StartsWith("using var ", StringComparison.Ordinal) &&
               !line.StartsWith("using (", StringComparison.Ordinal);
    }

    private static bool ReferencesBinOrObjArtifact(string value)
    {
        var normalized = value.Replace('\\', '/');
        return Regex.IsMatch(normalized, @"(^|/)(bin|obj)/", RegexOptions.IgnoreCase);
    }

    private static bool IsInIgnoredDirectory(string file)
    {
        var relative = Path.GetRelativePath(FindRepoRoot(), file)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return relative.Split('/')
            .Any(segment => segment is "bin" or "obj" or ".git");
    }

    private static string FormatPath(string file)
    {
        return Path.GetRelativePath(FindRepoRoot(), file);
    }

    private static string FindRepoRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
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
