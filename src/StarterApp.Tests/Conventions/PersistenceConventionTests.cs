using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using StarterApp.Api.Data;
using StarterApp.DbMigrator;

namespace StarterApp.Tests.Conventions;

public class PersistenceConventionTests : ConventionTestBase
{
    // === Entity Configuration ===

    [Fact]
    public void AllDomainEntities_MustBeRegisteredInDbContext()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));

        var dbSetProperties = typeof(ApplicationDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.IsGenericType &&
                   p.PropertyType.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();

        entityTypes
            .MustConformTo(new MustBeRegisteredInDbContextConvention(dbSetProperties))
            .WithFailureAssertion(Assert.Fail);
    }

    // === Value Object Configuration ===

    [Fact]
    public void ValueObjects_MustNotBeRegisteredAsDbSets()
    {
        var valueObjectTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("ValueObjects") &&
                   t.IsClass && !t.IsAbstract)
            .ToList();

        var dbSetTypes = typeof(ApplicationDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.IsGenericType &&
                   p.PropertyType.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();

        valueObjectTypes
            .MustConformTo(new MustNotBeRegisteredAsDbSetConvention(dbSetTypes))
            .WithFailureAssertion(Assert.Fail);
    }

    // === Enum Conventions ===

    [Fact]
    public void DomainEnumProperties_MustBeConfiguredWithStringConversion()
    {
        var domainEnumTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Enums") && t.IsEnum)
            .ToHashSet();

        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t))
            .ToList();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"enum-conventions-{Guid.NewGuid()}")
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var failures = new List<string>();

        foreach (var entityType in entityTypes)
        {
            var modelEntity = dbContext.Model.FindEntityType(entityType);
            if (modelEntity == null)
                continue;

            var enumProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => domainEnumTypes.Contains(p.PropertyType))
                .ToList();

            foreach (var enumProperty in enumProperties)
            {
                var mappedProperty = modelEntity.FindProperty(enumProperty.Name);
                if (mappedProperty == null)
                {
                    failures.Add($"{entityType.Name}.{enumProperty.Name} is a domain enum but is not mapped by EF Core.");
                    continue;
                }

                var converter = mappedProperty.GetTypeMapping().Converter;
                if (converter?.ProviderClrType != typeof(string))
                {
                    failures.Add($"{entityType.Name}.{enumProperty.Name} must be configured with HasConversion<string>() " +
                                 $"to keep the database contract stable. Provider type was {converter?.ProviderClrType.Name ?? "none"}.");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Domain enum properties must be persisted as strings:\n" + string.Join("\n", failures));
    }

    // === Data Access Conventions ===

    [Fact]
    public void DbContextTypes_MustNotHaveStaticMutableState()
    {
        var dbContextTypes = new[] { typeof(ApplicationDbContext) };

        dbContextTypes
            .MustConformTo(new MustNotHaveStaticMutableStateConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void Entities_MustNotHaveNavigationCollectionsWithPublicSetters()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));

        entityTypes
            .MustConformTo(new CollectionPropertiesMustNotHavePublicSettersConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void ConcurrencyCriticalEntities_MustUseRowVersionTokens()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"concurrency-conventions-{Guid.NewGuid()}")
            .Options;

        using var dbContext = new ApplicationDbContext(options);

        var requiredTokens = new Dictionary<Type, string[]>
        {
            [typeof(Order)] = [nameof(Order.RowVersion)],
            [typeof(Product)] = [nameof(Product.RowVersion)]
        };

        var failures = new List<string>();
        foreach (var (entityType, propertyNames) in requiredTokens)
        {
            var modelEntity = dbContext.Model.FindEntityType(entityType);
            if (modelEntity == null)
            {
                failures.Add($"{entityType.Name} is not mapped by EF Core.");
                continue;
            }

            foreach (var propertyName in propertyNames)
            {
                var property = modelEntity.FindProperty(propertyName);
                if (property == null)
                {
                    failures.Add($"{entityType.Name}.{propertyName} is missing from the EF model.");
                    continue;
                }

                if (!property.IsConcurrencyToken || property.ValueGenerated != ValueGenerated.OnAddOrUpdate)
                    failures.Add($"{entityType.Name}.{propertyName} must be configured with IsRowVersion() for optimistic concurrency.");
            }
        }

        Assert.True(failures.Count == 0,
            "Concurrency-critical entities must use SQL rowversion tokens:\n" + string.Join("\n", failures));
    }

    // === Migration Safety ===

    [Fact]
    public void DbMigratorAssembly_MustHaveEmbeddedScriptsWithNumberedPrefix()
    {
        var scriptsDir = ResolveScriptsDirectory();
        if (scriptsDir == null)
            return;

        var badScripts = Directory.GetFiles(scriptsDir, "*.sql")
            .Select(Path.GetFileName)
            .Where(f => f != null && !System.Text.RegularExpressions.Regex.IsMatch(f, @"^\d{4}_"))
            .ToList();

        Assert.Empty(badScripts);
    }

    [Fact]
    public void DbMigratorAssembly_MustEmbedAllSqlMigrationScripts()
    {
        var scriptsDir = ResolveScriptsDirectory();
        if (scriptsDir == null)
            return;

        var resources = typeof(DatabaseMigrationEngine).Assembly
            .GetManifestResourceNames()
            .ToHashSet(StringComparer.Ordinal);

        var missingResources = Directory.GetFiles(scriptsDir, "*.sql", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(scriptsDir, file))
            .Select(relativePath => relativePath.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.'))
            .Select(relativePath => $"StarterApp.DbMigrator.Scripts.{relativePath}")
            .Where(resourceName => !resources.Contains(resourceName))
            .ToList();

        Assert.True(missingResources.Count == 0,
            "DbUp migrations must be embedded resources so published migrator artifacts contain every script:\n" +
            string.Join("\n", missingResources));
    }

    [Fact]
    public void MigrationScripts_MustNameAllConstraintsExplicitly()
    {
        // Scripts 0001–0011 predate this rule. Enforce from 0012 onward.
        const int firstEnforcedScript = 12;

        var scriptsDir = ResolveScriptsDirectory();
        if (scriptsDir == null)
            return;

        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(scriptsDir, "*.sql").OrderBy(f => f))
        {
            var fileName = Path.GetFileName(file);
            if (!int.TryParse(fileName.AsSpan(0, 4), out var scriptNumber) || scriptNumber < firstEnforcedScript)
                continue;

            var sql = File.ReadAllText(file);
            CheckForAnonymousConstraints(fileName, sql, violations);
        }

        Assert.True(violations.Count == 0,
            $"Migration scripts must name all constraints explicitly (PK_Table, DF_Table_Column, CK_Table_Desc, FK_Table_Column):\n" +
            string.Join("\n", violations));
    }

    private static void CheckForAnonymousConstraints(string fileName, string sql, List<string> violations)
    {
        // Strip SQL comments (-- line comments and /* block comments */)
        var stripped = System.Text.RegularExpressions.Regex.Replace(sql, @"--[^\r\n]*", "");
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"/\*[\s\S]*?\*/", "");

        // Anonymous inline PRIMARY KEY (not preceded by CONSTRAINT keyword)
        if (System.Text.RegularExpressions.Regex.IsMatch(stripped,
            @"(?<!\bCONSTRAINT\s+\w+\s+)PRIMARY\s+KEY",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            violations.Add($"  {fileName}: anonymous PRIMARY KEY — use CONSTRAINT PK_TableName PRIMARY KEY");
        }

        // Anonymous inline DEFAULT in CREATE TABLE (column definition with DEFAULT but no CONSTRAINT keyword)
        // Matches: ColumnName TYPE ... DEFAULT value  (without preceding CONSTRAINT)
        if (System.Text.RegularExpressions.Regex.IsMatch(stripped,
            @"(?<!\bCONSTRAINT\s+\w+\s+)DEFAULT\s+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            // Exclude dynamic SQL patterns that DROP old defaults (legitimate use)
            var defaultMatches = System.Text.RegularExpressions.Regex.Matches(stripped,
                @"(?<!\bCONSTRAINT\s+\w+\s+)DEFAULT\s+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match match in defaultMatches)
            {
                // Allow ADD CONSTRAINT DF_xxx DEFAULT (the CONSTRAINT keyword comes before the name, not before DEFAULT)
                // Check the broader context: look back for CONSTRAINT keyword
                var preceding = stripped[..match.Index];
                var lastConstraint = preceding.LastIndexOf("CONSTRAINT", StringComparison.OrdinalIgnoreCase);
                if (lastConstraint >= 0)
                {
                    // Check there's a name between CONSTRAINT and DEFAULT with no semicolon/comma breaking the statement
                    var between = preceding[lastConstraint..];
                    if (!between.Contains(';') && !between.Contains(',') &&
                        System.Text.RegularExpressions.Regex.IsMatch(between, @"CONSTRAINT\s+\w+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        continue;
                }

                violations.Add($"  {fileName}: anonymous DEFAULT — use CONSTRAINT DF_Table_Column DEFAULT or ADD CONSTRAINT DF_Table_Column DEFAULT");
                break;
            }
        }

        // Anonymous inline CHECK (not preceded by CONSTRAINT keyword)
        if (System.Text.RegularExpressions.Regex.IsMatch(stripped,
            @"(?<!\bCONSTRAINT\s+\w+\s+)CHECK\s*\(",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            violations.Add($"  {fileName}: anonymous CHECK — use CONSTRAINT CK_Table_Description CHECK (...)");
        }

        // Anonymous FOREIGN KEY (not preceded by CONSTRAINT keyword)
        if (System.Text.RegularExpressions.Regex.IsMatch(stripped,
            @"(?<!\bCONSTRAINT\s+\w+\s+)FOREIGN\s+KEY",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            violations.Add($"  {fileName}: anonymous FOREIGN KEY — use CONSTRAINT FK_Table_Column FOREIGN KEY");
        }
    }

    private static string? ResolveScriptsDirectory()
    {
        var scriptsDir = Path.Combine(
            Path.GetDirectoryName(typeof(ApplicationDbContext).Assembly.Location)!,
            "..", "..", "..", "..", "StarterApp.DbMigrator", "Scripts");

        if (!Directory.Exists(scriptsDir))
        {
            scriptsDir = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "..", "..", "..", "..", "..", "StarterApp.DbMigrator", "Scripts"));
        }

        return Directory.Exists(scriptsDir) ? scriptsDir : null;
    }

    // === Custom Convention Specifications ===

    private class MustBeRegisteredInDbContextConvention : ConventionSpecification
    {
        private readonly HashSet<Type> _registeredTypes;

        public MustBeRegisteredInDbContextConvention(HashSet<Type> registeredTypes)
        {
            _registeredTypes = registeredTypes;
        }

        protected override string FailureMessage => "must be registered as a DbSet<T> in ApplicationDbContext";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            return _registeredTypes.Contains(type)
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} is a domain entity but has no corresponding DbSet<{type.Name}> in ApplicationDbContext");
        }
    }

    private class MustNotBeRegisteredAsDbSetConvention : ConventionSpecification
    {
        private readonly HashSet<Type> _dbSetTypes;

        public MustNotBeRegisteredAsDbSetConvention(HashSet<Type> dbSetTypes)
        {
            _dbSetTypes = dbSetTypes;
        }

        protected override string FailureMessage => "must not be registered as a DbSet (value objects should use OwnsOne)";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            return !_dbSetTypes.Contains(type)
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} is a value object but is registered as DbSet<{type.Name}>. Use OwnsOne() instead.");
        }
    }

    private class MustNotHaveStaticMutableStateConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must not have static mutable state (DbContext should be scoped, not singleton)";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var staticMutableFields = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => !f.IsLiteral && !f.IsInitOnly)
                .ToList();

            return staticMutableFields.Count == 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} has static mutable fields: {string.Join(", ", staticMutableFields.Select(f => f.Name))}. DbContext types must be scoped, not used as singletons with shared state.");
        }
    }

    private class CollectionPropertiesMustNotHavePublicSettersConvention : ConventionSpecification
    {
        protected override string FailureMessage => "collection navigation properties must not have public setters";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var collectionProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType != typeof(string) &&
                       typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) &&
                       p.GetSetMethod(false) != null)
                .ToList();

            return collectionProperties.Count == 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} has collection properties with public setters: {string.Join(", ", collectionProperties.Select(p => p.Name))}. Use private setters to prevent external mutation.");
        }
    }

}
