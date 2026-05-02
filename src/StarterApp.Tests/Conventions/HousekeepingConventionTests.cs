using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StarterApp.Tests.Conventions;

public class HousekeepingConventionTests : ConventionTestBase
{
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
