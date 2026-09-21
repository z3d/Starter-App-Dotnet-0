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
                   !IsCompilerGenerated(t))
            .ToList();

        // Guard against a vacuous pass: MustConformTo over an empty set "conforms" trivially, so if the
        // QueryHandler discovery filter ever matches zero types (suffix/namespace refactor) this test would
        // silently go green while checking nothing. Mirrors QueryHandlers_MustUsePostgresRetryPolicy.
        Assert.NotEmpty(queryHandlers);

        queryHandlers
            .MustConformTo(new MustNotUseSelectStarConvention())
            .WithFailureAssertion(Assert.Fail);
    }

    [Fact]
    public void QueryHandlers_MustUsePostgresRetryPolicy()
    {
        // Dapper reads must go through PostgresRetryPolicy.ExecuteAsync so transient
        // PostgreSQL faults are retried symmetrically with EF writes.
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
            var usesRetryPolicy = methods.Any(m => IlReferencesType(m, "PostgresRetryPolicy"));

            if (!usesRetryPolicy)
                failures.Add($"{handler.Name} uses IDbConnection but no method (or its async state machine) " +
                             "references PostgresRetryPolicy. Wrap Dapper calls in PostgresRetryPolicy.ExecuteAsync.");
        }

        Assert.True(failures.Count == 0,
            "Query handlers must wrap Dapper calls in PostgresRetryPolicy.ExecuteAsync:\n" +
            string.Join("\n", failures));
    }

    private static bool HasIDbConnectionField(Type type)
    {
        return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(f => f.FieldType == typeof(System.Data.IDbConnection));
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

}
