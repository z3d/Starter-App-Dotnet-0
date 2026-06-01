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
        return type.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length != 0 ||
               type.Name.Contains('<') ||
               type.Name.Contains('>') ||
               type.Name.StartsWith("<>") ||
               type.Name.Contains("d__") ||
               type.Name.Contains("c__DisplayClass") ||
               type.Name.Contains("__StaticArrayInitTypeSize") ||
               type.IsNested;
    }

    // A type's own methods plus the methods of its compiler-generated nested types
    // (async state machines, where the real work lives after `async` lowering).
    internal static IEnumerable<MethodInfo> GetAllMethodsIncludingStateMachines(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        return type.GetMethods(flags)
            .Concat(type.GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(nested => nested.GetMethods(flags)));
    }

    // Scan IL for call/callvirt opcodes whose target method's DeclaringType matches the given name.
    // Catches both direct calls and references inside compiler-generated async state machines.
    internal static bool IlReferencesType(MethodInfo method, string typeName)
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
            // call = 0x28, callvirt = 0x6F — both followed by a 4-byte metadata token.
            if (il[i] is not (0x28 or 0x6F))
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                var member = module.ResolveMember(token);
                if (member?.DeclaringType?.Name == typeName)
                    return true;
            }
            catch
            {
                // Unresolvable generic instantiation — skip.
            }

            i += 4;
        }

        return false;
    }
}
