namespace StarterApp.Tests.Conventions;

/// <summary>
/// Intentional business-rule and not-found signals must use DomainRuleException (409) and
/// EntityNotFoundException (404). ResolveExceptionStatusCode deliberately maps bare BCL
/// InvalidOperationException / KeyNotFoundException to 500: those types are thrown by the
/// BCL itself (LINQ .Single(), dictionary misses), so treating them as client faults would
/// disguise server bugs as 409/404 and hide them from 5xx alerting. This test keeps domain
/// types and application handlers from reintroducing them as control-flow signals.
/// </summary>
public class ExceptionConventionTests : ConventionTestBase
{
    private static readonly string[] ForbiddenControlFlowExceptions =
    [
        nameof(InvalidOperationException),
        nameof(KeyNotFoundException)
    ];

    [Fact]
    public void DomainTypes_MustNotConstructBclControlFlowExceptions()
    {
        var domainTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !IsCompilerGenerated(t))
            .ToList();

        Assert.NotEmpty(domainTypes);

        AssertNoForbiddenExceptionConstruction(domainTypes,
            "Domain types must throw DomainRuleException/EntityNotFoundException, not bare BCL exceptions");
    }

    [Fact]
    public void ApplicationTypes_MustNotConstructBclControlFlowExceptions()
    {
        var applicationTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !IsCompilerGenerated(t) &&
                   t.Namespace?.StartsWith("StarterApp.Api.Application") == true)
            .ToList();

        Assert.NotEmpty(applicationTypes);

        AssertNoForbiddenExceptionConstruction(applicationTypes,
            "Application commands/queries/handlers must throw DomainRuleException/EntityNotFoundException, not bare BCL exceptions");
    }

    private static void AssertNoForbiddenExceptionConstruction(IEnumerable<Type> types, string ruleDescription)
    {
        var failures = new List<string>();

        foreach (var type in types)
        {
            foreach (var method in GetAllMethodsIncludingStateMachines(type))
            {
                foreach (var exceptionName in ForbiddenControlFlowExceptions)
                {
                    if (ConstructsType(method, exceptionName))
                        failures.Add($"{type.Name}.{method.Name} constructs {exceptionName} — " +
                                     "use DomainRuleException (409) or EntityNotFoundException (404) instead.");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{ruleDescription}:\n" + string.Join("\n", failures.Distinct()));
    }

    // newobj (0x73) is not covered by IlReferencesType (call/field opcodes only), and
    // `throw new X(...)` lowers to newobj + throw — so constructor sites need their own walk.
    private static bool ConstructsType(MethodInfo method, string typeName)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null)
            return false;

        var module = method.Module;
        var found = false;

        IlInstructionWalker.Walk(il, (opcode, _, operandStart, operandSize) =>
        {
            if (found || opcode != 0x73)
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
}
