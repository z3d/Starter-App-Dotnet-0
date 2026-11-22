namespace StarterApp.Tests.Conventions;

// Which tables the migrations give a column, read from the scripts themselves; a table is named by its last segment, as queries usually write it.
internal static class MigrationColumns
{
    private static readonly Regex CreateTable = new(@"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<table>[\w.""]+)\s*\((?<body>.*?)\)\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex AlterTable = new(@"\bALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?(?<table>[\w.""]+)(?<body>[^;]*);", RegexOptions.IgnoreCase);

    public static IReadOnlyCollection<string> TablesWithColumn(IEnumerable<string> scripts, string column)
    {
        var columnDefinition = new Regex($@"(?:^|[(,])\s*{Regex.Escape(column)}\s+\w", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var addedColumn = new Regex($@"\bADD\s+(?:COLUMN\s+)?(?:IF\s+NOT\s+EXISTS\s+)?{Regex.Escape(column)}\b", RegexOptions.IgnoreCase);
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var script in scripts)
        {
            var sql = Regex.Replace(Regex.Replace(script, @"--[^\r\n]*", ""), @"/\*[\s\S]*?\*/", "");
            foreach (Match match in CreateTable.Matches(sql))
            {
                if (columnDefinition.IsMatch(match.Groups["body"].Value))
                    tables.Add(Name(match.Groups["table"].Value));
            }

            foreach (Match match in AlterTable.Matches(sql))
            {
                if (addedColumn.IsMatch(match.Groups["body"].Value))
                    tables.Add(Name(match.Groups["table"].Value));
            }
        }

        return tables;
    }

    private static string Name(string table) => table.Replace("\"", "", StringComparison.Ordinal).Split('.')[^1].ToLowerInvariant();
}
