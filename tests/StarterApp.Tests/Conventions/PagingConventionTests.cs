namespace StarterApp.Tests.Conventions;

// A list cut at a fixed number of rows drops the rest unseen: it pages, or is named here with its bound.
public class PagingConventionTests : ConventionTestBase
{
    private static readonly Dictionary<string, string> BoundedLists = new();

    private static readonly Dictionary<string, string> FixedBatches = new()
    {
        ["OutboxProcessor"] = "claims BatchSize messages per poll; the rest wait for the next poll, none is dropped",
    };

    private static List<(Type Query, Type Result)> Queries() => ApiAssembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t))
        .Select(t => (Query: t, Result: t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))?.GetGenericArguments()[0]))
        .Where(q => q.Result != null)
        .Select(q => (q.Query, q.Result!))
        .ToList();

    private static bool IsList(Type type) => type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) || type.GetGenericTypeDefinition() == typeof(IEnumerable<>));

    private static bool TakesAPage(Type query) =>
        query.GetProperty("Page")?.PropertyType == typeof(int) && query.GetProperty("PageSize")?.PropertyType == typeof(int);

    [Fact]
    public void ListQueries_ArePagedOrNamedWithTheirBound()
    {
        var queries = Queries();
        Assert.Contains(queries, q => IsList(q.Result) && TakesAPage(q.Query));

        var unpaged = queries.Where(q => IsList(q.Result) && !TakesAPage(q.Query) && !BoundedLists.ContainsKey(q.Query.Name))
            .Select(q => q.Query.Name).OrderBy(n => n).ToList();
        Assert.True(unpaged.Count == 0,
            "A query returning a list must take Page and PageSize or be named in PagingConventionTests with why its rows are bounded:\n" +
            string.Join("\n", unpaged));

        var unpagedLists = queries.Where(q => IsList(q.Result) && !TakesAPage(q.Query)).Select(q => q.Query.Name).ToHashSet();
        var stale = BoundedLists.Keys.Where(n => !unpagedLists.Contains(n)).OrderBy(n => n).ToList();
        Assert.True(stale.Count == 0, "Named in PagingConventionTests but no longer an unpaged list query; take it off:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void PagedQueries_FetchOneRowPastThePage()
    {
        var paged = Queries().Where(q => TakesAPage(q.Query)).Select(q => q.Query).ToList();
        Assert.NotEmpty(paged);

        var failures = paged
            .Select(query => (query, handler: ApiAssembly.GetType($"{query.FullName}Handler")))
            .Where(x => x.handler is null || !ExtractStringLiterals(x.handler).Any(sql => sql.Contains("LIMIT @FetchSize OFFSET @Offset", StringComparison.Ordinal)))
            .Select(x => x.query.Name)
            .ToList();
        Assert.True(failures.Count == 0,
            "A paged query's handler reads LIMIT @FetchSize OFFSET @Offset with FetchSize = PageSize + 1, so PagedAsync can say whether another page follows:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void Sql_CutsAListOnlyToThePage()
    {
        var limit = new Regex(@"\bLIMIT\s+(?!1\b)(?!@FetchSize\b)([0-9@:{]\S*)");
        var types = CoreProductionAssemblies.SelectMany(a => a.GetTypes()).Where(t => t.IsClass && !IsCompilerGenerated(t)).ToList();

        var cuts = types
            .SelectMany(t => ExtractStringLiterals(t).Select(sql => (t, m: limit.Match(sql))))
            .Where(x => x.m.Success)
            .ToList();
        Assert.Contains(cuts, x => FixedBatches.ContainsKey(x.t.Name));

        var failures = cuts
            .Where(x => !FixedBatches.ContainsKey(x.t.Name))
            .Select(x => $"{x.t.FullName}: LIMIT {x.m.Groups[1].Value}")
            .Distinct()
            .ToList();
        Assert.True(failures.Count == 0,
            "A list is cut only by the page (LIMIT @FetchSize), never by a fixed number that drops the rest unseen; LIMIT 1 is a lookup, and a batch is named in PagingConventionTests:\n" +
            string.Join("\n", failures));
    }
}
