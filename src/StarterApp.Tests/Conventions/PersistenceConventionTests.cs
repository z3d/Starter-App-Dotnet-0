namespace StarterApp.Tests.Conventions;

public class PersistenceConventionTests : ConventionTestBase
{
    // === Outbox Capture Wiring ===
    // The single-SaveChanges outbox pattern only holds if every EF write entry point funnels through
    // the domain-event capture step. A handler calling SaveChanges that bypassed capture would silently
    // drop domain events with no compile error. This scans the actual SaveChanges overrides' IL.

    [Fact]
    public void ApplicationDbContextSaveChanges_MustCaptureDomainEventsIntoOutbox()
    {
        var contextType = typeof(ApplicationDbContext);

        // Discover the capture method by behaviour rather than name: whichever instance method
        // materializes OutboxMessage rows. Renaming the private helper must not silently disable this test.
        var captureMethodNames = GetAllMethodsIncludingStateMachines(contextType)
            .Where(ReferencesOutboxMessage)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(captureMethodNames.Count > 0,
            "ApplicationDbContext must contain a method that captures domain events into OutboxMessage rows.");

        var entryPoints = new[]
        {
            ("SaveChanges(bool)", contextType.GetMethod(nameof(DbContext.SaveChanges), [typeof(bool)])),
            ("SaveChangesAsync(bool, CancellationToken)",
                contextType.GetMethod(nameof(DbContext.SaveChangesAsync), [typeof(bool), typeof(CancellationToken)])),
        };

        var violations = new List<string>();
        foreach (var (description, method) in entryPoints)
        {
            if (method is null || method.DeclaringType != contextType)
            {
                violations.Add($"{description} is not overridden on ApplicationDbContext; EF could persist aggregates without capturing their domain events.");
                continue;
            }

            if (!SelfAndStateMachine(method).Any(scannable => IlCallsMethodNamed(scannable, captureMethodNames)))
                violations.Add($"{description} does not call the outbox-capture method before persisting; domain events would be silently dropped.");
        }

        Assert.True(violations.Count == 0,
            "Every EF SaveChanges entry point on ApplicationDbContext must capture domain events into the outbox:\n" +
            string.Join("\n", violations));
    }

    // OutboxMessage.Create is referenced as a method group (`.Select(OutboxMessage.Create)`), which the
    // compiler emits as ldftn rather than call/callvirt. Scan the token-bearing member opcodes that can
    // carry an OutboxMessage reference: call, callvirt, newobj, and the two-byte ldftn/ldvirtftn.
    private static bool ReferencesOutboxMessage(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body == null)
            return false;

        var il = body.GetILAsByteArray();
        if (il == null)
            return false;

        var module = method.Module;

        for (var i = 0; i < il.Length - 4; i++)
        {
            int tokenOffset;
            if (il[i] is 0x28 or 0x6F or 0x73)
            {
                tokenOffset = i + 1;
            }
            else if (il[i] == 0xFE && i + 1 < il.Length && il[i + 1] is 0x06 or 0x07)
            {
                tokenOffset = i + 2;
            }
            else
            {
                continue;
            }

            if (tokenOffset + 4 > il.Length)
                break;

            var token = BitConverter.ToInt32(il, tokenOffset);
            try
            {
                if (module.ResolveMember(token)?.DeclaringType?.Name == nameof(OutboxMessage))
                    return true;
            }
            catch
            {
                // Unresolvable generic instantiation — not an OutboxMessage reference.
            }
        }

        return false;
    }

    private static IEnumerable<MethodInfo> SelfAndStateMachine(MethodInfo method)
    {
        yield return method;

        // For async methods the body that actually issues the calls lives in the generated state machine.
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        var moveNext = stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (moveNext != null)
            yield return moveNext;
    }

    private static bool IlCallsMethodNamed(MethodInfo method, IReadOnlySet<string> methodNames)
    {
        var body = method.GetMethodBody();
        if (body == null)
            return false;

        var il = body.GetILAsByteArray();
        if (il == null)
            return false;

        var module = method.Module;

        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                if (module.ResolveMember(token) is MethodBase called && methodNames.Contains(called.Name))
                    return true;
            }
            catch
            {
                // Unresolvable generic instantiation — not the method we are looking for.
            }

            i += 4;
        }

        return false;
    }

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
            "Concurrency-critical entities must use PostgreSQL xmin row version tokens:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void OwnerScopedEntities_MustPersistOwnerSubjectAndTenantId()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"owner-scope-conventions-{Guid.NewGuid()}")
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var ownerScopedEntities = new[] { typeof(Customer), typeof(Product), typeof(Order) };
        var failures = new List<string>();

        foreach (var entityType in ownerScopedEntities)
        {
            var modelEntity = dbContext.Model.FindEntityType(entityType);
            if (modelEntity == null)
            {
                failures.Add($"{entityType.Name} is not mapped by EF Core.");
                continue;
            }

            AssertOwnerScopeProperty(entityType, modelEntity, nameof(Customer.OwnerSubject), OwnershipDefaults.MaxOwnerSubjectLength, failures);
            AssertOwnerScopeProperty(entityType, modelEntity, nameof(Customer.TenantId), OwnershipDefaults.MaxTenantIdLength, failures);
        }

        Assert.True(failures.Count == 0,
            "Owner-scoped entities must persist required owner identity columns:\n" + string.Join("\n", failures));
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
        const int firstEnforcedScript = 1;

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
                var lineStart = stripped.LastIndexOf('\n', Math.Max(0, match.Index - 1));
                var lineEnd = stripped.IndexOf('\n', match.Index);
                if (lineEnd < 0)
                    lineEnd = stripped.Length;

                var line = stripped[(lineStart + 1)..lineEnd];
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"GENERATED\s+BY\s+DEFAULT\s+AS\s+IDENTITY", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    continue;

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

    private static void AssertOwnerScopeProperty(Type entityType, IEntityType modelEntity, string propertyName, int maxLength, List<string> failures)
    {
        var property = modelEntity.FindProperty(propertyName);
        if (property == null)
        {
            failures.Add($"{entityType.Name}.{propertyName} is missing from the EF model.");
            return;
        }

        if (property.IsNullable)
            failures.Add($"{entityType.Name}.{propertyName} must be required.");

        if (property.GetMaxLength() != maxLength)
            failures.Add($"{entityType.Name}.{propertyName} must have max length {maxLength}.");
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
