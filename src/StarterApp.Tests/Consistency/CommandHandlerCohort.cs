using StarterApp.Api.Infrastructure.Mediator;

namespace StarterApp.Tests.Consistency;

/// <summary>
/// Cohort definition for command handlers. Discovers all handlers in the API assembly,
/// extracts structural fingerprints, and names the pinned exemplars.
/// </summary>
public class CommandHandlerCohort : ICohortDefinition<HandlerFingerprint>
{
    private static readonly Assembly ApiAssembly = typeof(StarterApp.Api.Infrastructure.IApiMarker).Assembly;

    public string CohortName => "CommandHandlers";

    /// <summary>
    /// Must match docs/exemplars/command-handlers/README.md.
    /// The ExemplarAlignment convention test enforces this.
    /// </summary>
    public IReadOnlyList<string> ExemplarTypeNames =>
    [
        "CreateProductCommandHandler",
        "UpdateOrderStatusCommandHandler",
        "DeleteCustomerCommandHandler"
    ];

    public IReadOnlyList<Type> DiscoverTypes()
    {
        return ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("CommandHandler"))
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                 i.GetGenericTypeDefinition() == typeof(IRequestHandler<>))))
            .OrderBy(t => t.Name)
            .ToList();
    }

    public HandlerFingerprint Extract(Type handlerType)
    {
        var ctor = handlerType.GetConstructors().FirstOrDefault();
        var ctorParams = ctor?.GetParameters() ?? [];

        var allMethods = IlInspector.GetAllMethodsIncludingStateMachines(handlerType);
        var declaredMethods = handlerType.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly);

        return new HandlerFingerprint
        {
            TypeName = handlerType.Name,
            IlByteSize = IlInspector.SumIlByteSize(handlerType),
            ConstructorDependencyCount = ctorParams.Length,
            HasLogger = HasSerilogCalls(allMethods),
            HasCacheInvalidator = ctorParams.Any(p =>
                p.ParameterType == typeof(ICacheInvalidator)),
            HasTryCatch = IlInspector.HasExceptionHandling(allMethods),
            PrivateMethodCount = CountPrivateMethods(declaredMethods),
            EntityLoadCount = CountEntityLoads(allMethods)
        };
    }

    private static int CountPrivateMethods(MethodInfo[] declaredMethods)
    {
        return declaredMethods.Count(m =>
            (m.IsPrivate || m.IsAssembly) &&
            !m.IsSpecialName &&
            m.Name != ".ctor" &&
            !m.Name.StartsWith('<'));
    }

    private static int CountEntityLoads(IEnumerable<MethodInfo> methods)
    {
        var count = 0;
        foreach (var method in methods)
            count += IlInspector.CountMethodCallsByName(method,
                "FindAsync",
                "FirstOrDefaultAsync",
                "SingleOrDefaultAsync",
                "AnyAsync");

        return count;
    }

    private static bool HasSerilogCalls(IEnumerable<MethodInfo> methods)
    {
        foreach (var method in methods)
        {
            var count = IlInspector.CountMethodCallsByName(method,
                "Information",
                "Warning",
                "Error",
                "Debug");
            if (count > 0)
                return true;
        }

        return false;
    }
}
