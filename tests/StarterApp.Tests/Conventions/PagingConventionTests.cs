namespace StarterApp.Tests.Conventions;

// A list cut at a fixed number of rows drops the rest unseen: it pages, or is named here with its bound.
public class PagingConventionTests : ConventionTestBase
{
    private static readonly Dictionary<string, string> BoundedLists = new()
    {
        ["GetOrderByIdQuery.Items"] = "Order.MaxItems caps an order at 50 items in the domain",
    };

    private static readonly Dictionary<string, string> FixedBatches = new()
    {
        ["OutboxProcessor"] = "claims BatchSize messages per poll; the rest wait for the next poll, none is dropped",
    };

    private static readonly Dictionary<string, string> TakenInMemory = new()
    {
        ["CorrelationContext"] = "shortens a correlation id's characters, not rows",
        ["OutboxProcessor"] = "the EF fallback of the same BatchSize claim",
        ["PayloadBlobNaming"] = "shortens a blob path segment's characters, not rows",
        ["PayloadEntityReferenceExtractor"] = "caps the ids indexed from one payload and marks the record entityReferencesTruncated",
    };

    internal static readonly Regex Cut = new(
        @"\b(?:LIMIT|FETCH\s+(?:FIRST|NEXT))\s*(?:$|(?!1\b)(?!@FetchSize\b)([0-9@:{(]\S*))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<(Type Query, Type Result)> Queries() => ApiAssembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t))
        .Select(t => (Query: t, Result: ResultOf(t)))
        .Where(q => q.Result != null)
        .Select(q => (q.Query, q.Result!))
        .ToList();

    internal static Type? ResultOf(Type query) =>
        query.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition().Name == "IQuery`1")?.GetGenericArguments()[0];

    internal static bool IsList(Type type) => ElementOf(type) is not null;

    internal static Type? ElementOf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string) || type == typeof(byte[]))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        var sequence = type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return sequence?.GetGenericArguments()[0] ?? (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ? typeof(object) : null);
    }

    internal static bool TakesAPage(Type query) =>
        query.GetProperty("Page")?.PropertyType == typeof(int) && query.GetProperty("PageSize")?.PropertyType == typeof(int);

    internal static List<string> NestedLists(Type result)
    {
        var found = new List<string>();
        Walk(ElementOf(result) ?? result, "", []);
        return found;

        void Walk(Type type, string path, HashSet<Type> seen)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                foreach (var argument in type.GetGenericArguments())
                    Walk(argument, path, seen);
                return;
            }

            if (type.IsEnum || !seen.Add(type))
                return;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                var here = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                if (ElementOf(property.PropertyType) is { } element)
                {
                    found.Add(here);
                    Walk(element, here, seen);
                }
                else
                {
                    Walk(property.PropertyType, here, seen);
                }
            }

            seen.Remove(type);
        }
    }

    internal static List<string> Unbounded(IEnumerable<(Type Query, Type Result)> queries, IReadOnlyDictionary<string, string> bounded) => queries
        .SelectMany(q => (IsList(q.Result) && !TakesAPage(q.Query) ? new[] { q.Query.Name } : [])
            .Concat(NestedLists(q.Result).Select(path => $"{q.Query.Name}.{path}")))
        .Where(name => !bounded.ContainsKey(name))
        .Order(StringComparer.Ordinal)
        .ToList();

    [Fact]
    public void ListQueries_ArePagedOrNamedWithTheirBound()
    {
        var queries = Queries();
        Assert.Contains(queries, q => IsList(q.Result) && TakesAPage(q.Query));

        var unpaged = Unbounded(queries, BoundedLists);
        Assert.True(unpaged.Count == 0,
            "A query returning a list must take Page and PageSize, and a list inside a result is named in PagingConventionTests (Query.Path) with why its rows are bounded:\n" +
            string.Join("\n", unpaged));

        var every = Unbounded(queries, new Dictionary<string, string>()).ToHashSet();
        var stale = BoundedLists.Keys.Where(n => !every.Contains(n)).OrderBy(n => n).ToList();
        Assert.True(stale.Count == 0, "Named in PagingConventionTests but no longer an unpaged list; take it off:\n" + string.Join("\n", stale));
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
        var types = CoreProductionAssemblies.SelectMany(a => a.GetTypes()).Where(t => t.IsClass && !IsCompilerGenerated(t)).ToList();

        var cuts = types
            .SelectMany(t => ExtractStringLiterals(t).Select(sql => (t, m: Cut.Match(sql))))
            .Where(x => x.m.Success)
            .ToList();
        Assert.Contains(cuts, x => FixedBatches.ContainsKey(x.t.Name));

        var failures = cuts
            .Where(x => !FixedBatches.ContainsKey(x.t.Name))
            .Select(x => $"{x.t.FullName}: {x.m.Value.Trim()}")
            .Distinct()
            .ToList();
        Assert.True(failures.Count == 0,
            "A list is cut only by the page (LIMIT @FetchSize), never by a fixed or runtime number that drops the rest unseen; LIMIT 1 is a lookup, and a batch is named in PagingConventionTests:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void Code_TakesTheFirstRowsOfAListOnlyWhereNamed()
    {
        var takers = TypesThatTake(CoreProductionAssemblies.SelectMany(a => a.GetTypes()));
        Assert.Contains(takers, name => TakenInMemory.ContainsKey(name));

        var failures = takers.Where(name => !TakenInMemory.ContainsKey(name)).ToList();
        Assert.True(failures.Count == 0,
            ".Take(n) drops the rest of a list unseen: page instead, or name the type in PagingConventionTests with what happens to the rest:\n" +
            string.Join("\n", failures));

        var stale = TakenInMemory.Keys.Where(name => !takers.Contains(name)).ToList();
        Assert.True(stale.Count == 0, "Named in PagingConventionTests but no longer calls Take; take it off:\n" + string.Join("\n", stale));
    }

    internal static List<string> TypesThatTake(IEnumerable<Type> types) => types
        .Where(t => t.IsClass && !IsCompilerGenerated(t))
        .Where(t => GetAllMethodsIncludingStateMachines(t).Any(m => IlReferencesMember(m, "Queryable", "Take") || IlReferencesMember(m, "Enumerable", "Take")))
        .Select(t => t.Name)
        .Distinct()
        .Order(StringComparer.Ordinal)
        .ToList();

    [Theory]
    [InlineData("SELECT id FROM t ORDER BY id LIMIT 50")]
    [InlineData("select id from t order by id limit 50")]
    [InlineData("SELECT id FROM t LIMIT @Max")]
    [InlineData("SELECT id FROM t LIMIT {0}")]
    [InlineData("SELECT id FROM t LIMIT ")]
    [InlineData("SELECT id FROM t LIMIT")]
    [InlineData("SELECT id FROM t LIMIT (SELECT 5)")]
    [InlineData("SELECT id FROM t FETCH FIRST 20 ROWS ONLY")]
    [InlineData("SELECT id FROM t OFFSET 0 ROWS fetch next @n rows only")]
    public void TheCutPattern_SeesEveryWayOfCuttingAList(string sql) => Assert.Matches(Cut, sql);

    [Theory]
    [InlineData("SELECT id FROM t LIMIT @FetchSize OFFSET @Offset")]
    [InlineData("SELECT id FROM t LIMIT 1")]
    [InlineData("SELECT id FROM t FETCH FIRST 1 ROW ONLY")]
    [InlineData("SELECT id FROM t FETCH FIRST ROW ONLY")]
    [InlineData("SELECT rate_limit FROM limits")]
    public void TheCutPattern_LeavesThePageAndTheLookupAlone(string sql) => Assert.DoesNotMatch(Cut, sql);

    [Fact]
    public void TheListCheck_SeesNestedListsArraysAndUnpagedLists_InSyntheticQueries()
    {
        var queries = new[] { typeof(Synthetic.Paged), typeof(Synthetic.Unpaged), typeof(Synthetic.Single), typeof(Synthetic.Scalar) }
            .Select(query => (query, ResultOf(query)!))
            .ToList();

        Assert.Equal(
            ["Paged.Lines", "Paged.Lines.Tags", "Scalar.Tags", "Single.Rows", "Single.Rows.Lines", "Single.Rows.Lines.Tags", "Unpaged", "Unpaged.Tags"],
            Unbounded(queries, new Dictionary<string, string>()));
        Assert.Equal(["Paged.Lines.Tags"], Unbounded(queries.Take(1), new Dictionary<string, string> { ["Paged.Lines"] = "bounded" }));
        Assert.Equal(["PagingConventionTests"], TypesThatTake([typeof(PagingConventionTests), typeof(Synthetic)]));
    }

    private static class Synthetic
    {
        public interface IQuery<TResult>;

        public sealed record Line(int Id, string[] Tags, byte[] Hash, string Name);

        public sealed record Row(int Id, IReadOnlyList<Line> Lines, DateTimeOffset At, Row? Parent);

        public sealed record Holder(Dictionary<string, Row> Rows, Holder? Child, int? Count);

        public sealed record Paged(int Page, int PageSize) : IQuery<IEnumerable<Row>>;

        public sealed record Unpaged : IQuery<Line[]>;

        public sealed record Single : IQuery<Holder?>;

        public sealed record Scalar : IQuery<Line?>;
    }
}
