using StarterApp.Api.Infrastructure;
using StarterApp.DbMigrator;

namespace StarterApp.Tests.Conventions;

public abstract class ConventionTestBase
{
    protected static readonly Assembly DomainAssembly = typeof(Product).Assembly;
    protected static readonly Assembly ApiAssembly = typeof(IApiMarker).Assembly;
    protected static readonly Assembly DbMigratorAssembly = typeof(DatabaseMigrationEngine).Assembly;

    protected static readonly Assembly[] CoreProductionAssemblies =
    [
        ApiAssembly,
        DomainAssembly,
        DbMigratorAssembly
    ];

    protected static bool IsCompilerGenerated(Type type)
    {
        return type.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Any() ||
               type.Name.Contains("<") ||
               type.Name.Contains(">") ||
               type.Name.StartsWith("<>") ||
               type.Name.Contains("d__") ||
               type.Name.Contains("c__DisplayClass") ||
               type.Name.Contains("__StaticArrayInitTypeSize") ||
               type.IsNested;
    }
}
