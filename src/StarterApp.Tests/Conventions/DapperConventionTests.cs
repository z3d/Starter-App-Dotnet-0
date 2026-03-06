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
