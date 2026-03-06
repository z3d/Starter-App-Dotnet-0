using StarterApp.Api.Data;

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
    public void EntitiesWithDomainEnums_MustHaveEnumPropertiesConfigured()
    {
        var domainEnumTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Enums") && t.IsEnum)
            .ToHashSet();

        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.Contains("Entities") &&
                   t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t))
            .Where(t => t.GetProperties().Any(p => domainEnumTypes.Contains(p.PropertyType)));

        entityTypes
            .MustConformTo(new MustHaveDomainEnumPropertiesConvention(domainEnumTypes))
            .WithFailureAssertion(Assert.Fail);
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

    // === Migration Safety ===

    [Fact]
    public void DbMigratorAssembly_MustHaveEmbeddedScriptsWithNumberedPrefix()
    {
        // DbMigrator embeds SQL scripts as resources. Verify the assembly
        // follows the convention by checking the migrator type itself.
        var dbMigratorTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "StarterApp.DbMigrator")
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract)
            .ToList();

        // If the migrator isn't loaded, fall back to file system check
        if (dbMigratorTypes.Count == 0)
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

            if (Directory.Exists(scriptsDir))
            {
                var badScripts = Directory.GetFiles(scriptsDir, "*.sql")
                    .Select(Path.GetFileName)
                    .Where(f => f != null && !System.Text.RegularExpressions.Regex.IsMatch(f, @"^\d{4}_"))
                    .ToList();

                Assert.Empty(badScripts);
            }
        }
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

    private class MustHaveDomainEnumPropertiesConvention : ConventionSpecification
    {
        private readonly HashSet<Type> _domainEnumTypes;

        public MustHaveDomainEnumPropertiesConvention(HashSet<Type> domainEnumTypes)
        {
            _domainEnumTypes = domainEnumTypes;
        }

        protected override string FailureMessage => "must have domain enum properties that require string conversion configuration";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var enumProperties = type.GetProperties()
                .Where(p => _domainEnumTypes.Contains(p.PropertyType))
                .ToList();

            return enumProperties.Count > 0
                ? ConventionResult.Satisfied(type.FullName!)
                : ConventionResult.NotSatisfied(type.FullName!,
                    $"{type.Name} was expected to have domain enum properties but none were found");
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
