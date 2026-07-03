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
    // (async state machines, where the real work lives after `async` lowering), walking same-assembly
    // base classes too: a handler whose HandleAsync lives on a shared command-handler base class must
    // still have that IL scanned, or behaviour conventions would silently skip every derived handler.
    internal static IEnumerable<MethodInfo> GetAllMethodsIncludingStateMachines(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.Assembly != type.Assembly)
                break;

            // Closed generic bases must be opened to their definition to read method bodies.
            var inspectable = current.IsGenericType && !current.IsGenericTypeDefinition
                ? current.GetGenericTypeDefinition()
                : current;

            foreach (var method in inspectable.GetMethods(flags))
                yield return method;

            foreach (var nested in inspectable.GetNestedTypes(BindingFlags.NonPublic))
                foreach (var method in nested.GetMethods(flags))
                    yield return method;
        }
    }

    // Scan IL for method/field opcodes whose target member's DeclaringType matches the given name.
    // Catches both direct references and references inside compiler-generated async state machines.
    // Walks on instruction boundaries via IlInstructionWalker so an operand byte (e.g. inside an
    // ldc.i4 constant or branch target) can never be misread as an opcode.
    internal static bool IlReferencesType(MethodInfo method, string typeName)
    {
        var body = method.GetMethodBody();
        if (body == null)
            return false;

        var il = body.GetILAsByteArray();
        if (il == null)
            return false;

        var module = method.Module;
        var found = false;

        IlInstructionWalker.Walk(il, (opcode, _, operandStart, operandSize) =>
        {
            // call/callvirt plus field access opcodes — each followed by a 4-byte metadata token.
            if (found || opcode is not (0x28 or 0x6F or 0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x80))
                return;

            if (operandSize < 4 || operandStart + 3 >= il.Length)
                return;

            var token = BitConverter.ToInt32(il, operandStart);
            try
            {
                var member = module.ResolveMember(token);
                if (member?.DeclaringType?.Name == typeName)
                    found = true;
            }
            catch
            {
                // Unresolvable generic instantiation — skip.
            }
        });

        return found;
    }

    internal static IEnumerable<string> ExtractStringLiterals(Type type)
    {
        return GetAllMethodsIncludingStateMachines(type)
            .SelectMany(ExtractStringLiterals);
    }

    // Operand-safe via IlInstructionWalker: only genuine ldstr instructions are resolved.
    internal static IEnumerable<string> ExtractStringLiterals(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null || il.Length < 5)
            return [];

        var module = method.Module;
        var literals = new List<string>();

        IlInstructionWalker.Walk(il, (opcode, _, operandStart, operandSize) =>
        {
            if (opcode != 0x72 || operandSize < 4 || operandStart + 3 >= il.Length)
                return;

            var token = BitConverter.ToInt32(il, operandStart);
            try
            {
                literals.Add(module.ResolveString(token));
            }
            catch
            {
                // Not a valid string token — skip.
            }
        });

        return literals;
    }
}
