namespace StarterApp.Tests.Conventions;

// Every persisted aggregate carries a created/updated pair, and both reach the database. "When did
// this row first exist" and "when did it last change" must be answerable for every table without
// reconstructing history from logs. The EF model and the DbUp scripts are independent sources of
// truth, so all three layers are checked: the property, the mapping, and the migrated column.
public class AuditTimestampConventionTests : ConventionTestBase
{
    private const string CreatedProperty = "DateCreated";
    private const string UpdatedProperty = "LastUpdated";

    private static readonly (Type Aggregate, string Table)[] Aggregates =
    [
        (typeof(Customer), "customers"),
        (typeof(Product), "products"),
        (typeof(Order), "orders")
    ];

    [Fact]
    public void EveryAggregate_MustExposeANonNullableCreatedAndUpdatedTimestamp()
    {
        var failures = new List<string>();

        foreach (var (aggregate, _) in Aggregates)
            foreach (var propertyName in new[] { CreatedProperty, UpdatedProperty })
            {
                var property = aggregate.GetProperty(propertyName);
                if (property is null)
                    failures.Add($"{aggregate.Name} has no {propertyName} property.");
                else if (property.PropertyType != typeof(DateTimeOffset))
                    failures.Add($"{aggregate.Name}.{propertyName} must be a non-nullable DateTimeOffset (found {property.PropertyType.Name}).");
            }

        Assert.True(failures.Count == 0,
            "Every aggregate needs a non-nullable DateCreated/LastUpdated pair:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void EveryAggregate_MustMapBothTimestampsAsRequiredColumns()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit-timestamps-{Guid.NewGuid()}")
            .Options;
        using var dbContext = new ApplicationDbContext(options);
        var failures = new List<string>();

        foreach (var (aggregate, _) in Aggregates)
        {
            var entity = dbContext.Model.FindEntityType(aggregate);
            if (entity is null)
            {
                failures.Add($"{aggregate.Name} is not part of the EF model.");
                continue;
            }

            foreach (var propertyName in new[] { CreatedProperty, UpdatedProperty })
            {
                var mapped = entity.FindProperty(propertyName);
                if (mapped is null)
                    failures.Add($"{aggregate.Name}.{propertyName} is not mapped.");
                else if (mapped.IsNullable)
                    failures.Add($"{aggregate.Name}.{propertyName} is mapped as nullable.");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryAggregateTable_MustDeclareBothTimestampColumnsInAMigration()
    {
        var scriptsDirectory = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.DbMigrator", "Scripts");
        var failures = new List<string>();

        foreach (var (aggregate, table) in Aggregates)
        {
            // A column may arrive with the table or in a later ALTER; either way some script must
            // add it to this table.
            var statements = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(File.ReadAllText)
                .SelectMany(sql => sql.Split(';'))
                .Where(statement => Regex.IsMatch(statement, $@"\b(CREATE|ALTER)\s+TABLE\s+{table}\b", RegexOptions.IgnoreCase))
                .ToList();

            foreach (var column in new[] { "date_created", "last_updated" })
                if (!statements.Any(statement => Regex.IsMatch(statement, $@"\b{column}\s+timestamptz\s+NOT\s+NULL\b", RegexOptions.IgnoreCase)))
                    failures.Add($"No migration declares '{column} timestamptz NOT NULL' on {table} (for {aggregate.Name}).");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
