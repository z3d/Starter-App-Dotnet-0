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

    // Scan IL for method/field opcodes whose target member's DeclaringType matches the given name.
    // Catches both direct references and references inside compiler-generated async state machines.
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
            // call/callvirt plus field access opcodes — each followed by a 4-byte metadata token.
            if (il[i] is not (0x28 or 0x6F or 0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x80))
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

    internal static IEnumerable<string> ExtractStringLiterals(Type type)
    {
        return GetAllMethodsIncludingStateMachines(type)
            .SelectMany(ExtractStringLiterals);
    }

    internal static IEnumerable<string> ExtractStringLiterals(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body == null)
            yield break;

        var il = body.GetILAsByteArray();
        if (il == null || il.Length < 5)
            yield break;

        var module = method.Module;

        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] != 0x72)
                continue;

            var token = BitConverter.ToInt32(il, i + 1);
            string? resolved = null;
            try
            {
                resolved = module.ResolveString(token);
            }
            catch
            {
                // Not a valid string token — skip.
            }

            if (resolved != null)
                yield return resolved;

            i += 4;
        }
    }
}
