namespace StarterApp.Tests.Conventions;

// A subquery without the predicates passes only when it names an alias of an enclosing SELECT that passes.
internal static partial class SqlSelectBlocks
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "where", "on", "left", "right", "inner", "outer", "full", "cross", "join", "natural", "group", "order",
        "limit", "offset", "union", "intersect", "except", "having", "window", "using", "for", "returning", "set", "lateral",
    };

    public static IReadOnlyList<string> Unscoped(string sql, Regex tables, Func<string, bool> isScoped)
    {
        var subqueries = new List<string>();
        var collapsed = Collapse(Clean(sql), subqueries);
        var failures = new List<string>();
        Check(collapsed, new HashSet<string>(), subqueries, tables, isScoped, failures);
        return failures;
    }

    private static void Check(string block, IReadOnlySet<string> outerAliases, List<string> subqueries, Regex tables, Func<string, bool> isScoped, List<string> failures)
    {
        foreach (var branch in SetOperationOrStatement().Split(block).Where(part => !string.IsNullOrWhiteSpace(part)))
        {
            var defined = Aliases(branch);
            var references = TableReference().Replace(branch, " ");
            var correlated = outerAliases.Except(defined, StringComparer.OrdinalIgnoreCase)
                .Any(alias => Regex.IsMatch(references, $@"\b{Regex.Escape(alias)}\.", RegexOptions.IgnoreCase));
            var passes = isScoped(branch) || correlated;

            if (tables.IsMatch(branch) && !passes)
                failures.Add(Expand(branch, subqueries).Trim());

            var inherited = passes ? outerAliases.Union(defined, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            foreach (Match placeholder in Placeholder().Matches(branch))
                Check(subqueries[int.Parse(placeholder.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture)], inherited, subqueries, tables, isScoped, failures);
        }
    }

    private static string Clean(string sql)
    {
        return Regex.Replace(sql, @"'(?:[^']|'')*'|/\*[\s\S]*?\*/|--[^\r\n]*", match => match.Value[0] == '\'' ? "''" : " ");
    }

    private static string Collapse(string sql, List<string> subqueries)
    {
        var result = new System.Text.StringBuilder();
        var i = 0;
        while (i < sql.Length)
        {
            if (sql[i] != '(')
            {
                result.Append(sql[i++]);
                continue;
            }

            var close = MatchingParenthesis(sql, i);
            var inner = Collapse(sql[(i + 1)..close], subqueries);
            if (Regex.IsMatch(inner, @"^\s*(?:SELECT|WITH)\b", RegexOptions.IgnoreCase))
            {
                subqueries.Add(inner);
                result.Append(System.Globalization.CultureInfo.InvariantCulture, $"(§{subqueries.Count - 1})");
            }
            else
            {
                result.Append('(').Append(inner).Append(')');
            }

            i = close + 1;
        }

        return result.ToString();
    }

    private static int MatchingParenthesis(string sql, int open)
    {
        var depth = 0;
        for (var i = open; i < sql.Length; i++)
        {
            depth += sql[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
                return i;
        }

        return sql.Length - 1;
    }

    private static HashSet<string> Aliases(string branch)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TableReference().Matches(branch))
        {
            aliases.Add(match.Groups["table"].Value.Split('.')[^1]);
            if (match.Groups["alias"].Success && !Keywords.Contains(match.Groups["alias"].Value))
                aliases.Add(match.Groups["alias"].Value);
        }

        return aliases;
    }

    private static string Expand(string branch, List<string> subqueries) =>
        Placeholder().Replace(branch, match => "(" + Expand(subqueries[int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture)], subqueries) + ")");

    [GeneratedRegex(@";|\b(?:UNION|INTERSECT|EXCEPT)\b(?:\s+(?:ALL|DISTINCT)\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex SetOperationOrStatement();

    [GeneratedRegex(@"\(§(?<index>\d+)\)")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\b(?:FROM|JOIN)\s+(?:ONLY\s+)?(?<table>[\w.]+)(?:\s+(?:AS\s+)?(?<alias>\w+))?", RegexOptions.IgnoreCase)]
    private static partial Regex TableReference();
}
