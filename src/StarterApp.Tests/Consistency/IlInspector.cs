namespace StarterApp.Tests.Consistency;

/// <summary>
/// Reusable IL inspection utilities for any cohort's fingerprint extraction.
/// </summary>
public static class IlInspector
{
    /// <summary>
    /// Summed IL body size across a type's methods (including async state machines) in bytes.
    /// Used as a complexity proxy — never as a source-line count. The previous
    /// <c>size * 0.1 + 10</c> transform was a misleading approximation (2-4x actual lines,
    /// per-handler ratio varying, Spearman vs source = 0.54) so this returns the raw total.
    /// </summary>
    public static int SumIlByteSize(Type type)
    {
        var totalIlSize = 0;

        foreach (var method in GetAllMethodsIncludingStateMachines(type))
        {
            var body = method.GetMethodBody();
            if (body?.GetILAsByteArray() is { } il)
                totalIlSize += il.Length;
        }

        return totalIlSize;
    }

    public static bool HasExceptionHandling(IEnumerable<MethodInfo> methods)
    {
        foreach (var method in methods)
        {
            var body = method.GetMethodBody();
            if (body == null)
                continue;

            var catchClauses = body.ExceptionHandlingClauses
                .Count(c => c.Flags == ExceptionHandlingClauseOptions.Clause);

            if (catchClauses > 0)
            {
                var isStateMachine = method.DeclaringType != null &&
                    method.DeclaringType.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length > 0;

                if (isStateMachine)
                {
                    if (catchClauses > 1)
                        return true;
                }
                else
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static int CountMethodCallsByName(MethodInfo method, params string[] methodNames)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null || il.Length < 5)
            return 0;

        var count = 0;
        var module = method.Module;

        var typeArgs = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;

        IlInstructionWalker.Walk(il, (opcode, _, operandStart, operandSize) =>
        {
            if (opcode is not (0x28 or 0x6F) || operandSize < 4 || operandStart + 3 >= il.Length)
                return;

            var token = BitConverter.ToInt32(il, operandStart);
            try
            {
                var resolved = module.ResolveMethod(token, typeArgs, methodArgs);
                if (resolved != null && methodNames.Contains(resolved.Name))
                    count++;
            }
            catch
            {
                // Not a valid method token
            }
        });

        return count;
    }

    /// <summary>
    /// Counts case-insensitive occurrences of <paramref name="substring"/> across all
    /// string literals emitted by the type's methods (including async state machines).
    /// Resolves <c>ldstr</c> tokens via the module's metadata; operand-safe via
    /// <see cref="IlInstructionWalker"/> so non-opcode bytes aren't misread.
    /// </summary>
    public static int CountSubstringInStringLiterals(Type type, string substring)
    {
        var count = 0;
        var comparison = StringComparison.OrdinalIgnoreCase;

        foreach (var method in GetAllMethodsIncludingStateMachines(type))
        {
            var body = method.GetMethodBody();
            var il = body?.GetILAsByteArray();
            if (il is null || il.Length < 5)
                continue;

            var module = method.Module;

            IlInstructionWalker.Walk(il, (opcode, _, operandStart, operandSize) =>
            {
                if (opcode != 0x72 || operandSize < 4 || operandStart + 3 >= il.Length)
                    return;

                var token = BitConverter.ToInt32(il, operandStart);
                try
                {
                    var s = module.ResolveString(token);
                    var idx = 0;
                    while ((idx = s.IndexOf(substring, idx, comparison)) >= 0)
                    {
                        count++;
                        idx += substring.Length;
                    }
                }
                catch
                {
                    // Unresolvable tokens are not literals we can count.
                }
            });
        }

        return count;
    }

    public static IEnumerable<MethodInfo> GetAllMethodsIncludingStateMachines(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        return type.GetMethods(flags)
            .Concat(type.GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(nested => nested.GetMethods(flags)));
    }
}
