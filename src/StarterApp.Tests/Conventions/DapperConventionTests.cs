using System.Text.RegularExpressions;

namespace StarterApp.Tests.Conventions;

/// <summary>
/// Dapper query convention tests inspired by andrewabest/Tailor.
/// Enforces SQL best practices at test time by inspecting IL string literals
/// in query handler methods (including compiler-generated async state machines).
/// </summary>
public class DapperConventionTests : ConventionTestBase
{
    [Fact]
    public void QueryHandlers_MustNotUseSelectStar()
    {
        var queryHandlers = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.Name.EndsWith("QueryHandler") &&
                   !IsCompilerGenerated(t));

        queryHandlers
            .MustConformTo(new MustNotUseSelectStarConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void QueryHandlers_MustUseSqlRetryPolicy()
    {
        // Dapper reads must go through SqlRetryPolicy.ExecuteAsync so transient Azure SQL
        // faults (failover, throttling, network blips) are retried symmetrically with EF
        // writes (which use EnableRetryOnFailure). A plain _connection.Query* call would
        // surface a single transient fault as a 500 to the client.
        var queryHandlers = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.Name.EndsWith("QueryHandler") &&
                   !IsCompilerGenerated(t))
            .Where(HasIDbConnectionField)
            .ToList();

        Assert.NotEmpty(queryHandlers);

        var failures = new List<string>();

        foreach (var handler in queryHandlers)
        {
            var methods = GetAllMethodsIncludingStateMachines(handler);
            var usesRetryPolicy = methods.Any(m => IlReferencesType(m, "SqlRetryPolicy"));

            if (!usesRetryPolicy)
                failures.Add($"{handler.Name} uses IDbConnection but no method (or its async state machine) " +
                             "references SqlRetryPolicy. Wrap Dapper calls in SqlRetryPolicy.ExecuteAsync.");
        }

        Assert.True(failures.Count == 0,
            "Query handlers must wrap Dapper calls in SqlRetryPolicy.ExecuteAsync:\n" +
            string.Join("\n", failures));
    }

    private static bool HasIDbConnectionField(Type type)
    {
        return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(f => f.FieldType == typeof(System.Data.IDbConnection));
    }

    // Scan IL for call/callvirt opcodes whose target method's DeclaringType matches the given name.
    // Catches both direct calls (SqlRetryPolicy.ExecuteAsync) and references inside compiler-
    // generated async state machines (where the real work lives after `async` lowering).
    private static bool IlReferencesType(MethodInfo method, string typeName)
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

    /// <summary>
    /// Ensures Dapper query handlers never use SELECT * — columns must be explicitly listed.
    /// Inspects IL string literals in both the handler methods and compiler-generated async
    /// state machines (async method bodies are compiled into nested types).
    /// </summary>
    private class MustNotUseSelectStarConvention : ConventionSpecification
    {
        protected override string FailureMessage => "must not use SELECT * in Dapper queries";

        public override ConventionResult IsSatisfiedBy(Type type)
        {
            var methods = GetAllMethodsIncludingStateMachines(type);

            foreach (var method in methods)
            {
                foreach (var literal in ExtractStringLiterals(method))
                {
                    if (Regex.IsMatch(literal, @"SELECT\s+\*", RegexOptions.IgnoreCase))
                    {
                        return ConventionResult.NotSatisfied(type.FullName!,
                            $"{type.Name} uses SELECT * in a SQL query — explicitly list columns instead");
                    }
                }
            }

            return ConventionResult.Satisfied(type.FullName!);
        }
    }

    private static IEnumerable<MethodInfo> GetAllMethodsIncludingStateMachines(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        return type.GetMethods(flags)
            .Concat(type.GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(nested => nested.GetMethods(flags)));
    }

    /// <summary>
    /// Extracts string literals from a method's IL by scanning for ldstr (0x72) opcodes
    /// and resolving their metadata tokens.
    /// </summary>
    private static IEnumerable<string> ExtractStringLiterals(MethodInfo method)
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
            { resolved = module.ResolveString(token); }
            catch { /* not a valid string token — skip */ }

            if (resolved != null)
                yield return resolved;
        }
    }
}
