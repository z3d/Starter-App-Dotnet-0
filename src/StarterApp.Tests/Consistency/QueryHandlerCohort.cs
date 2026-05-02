using StarterApp.Api.Infrastructure.Mediator;

namespace StarterApp.Tests.Consistency;

/// <summary>
/// Cohort definition for query handlers. Queries use Dapper + IDbConnection (not EF Core),
/// return either a single read-model, a plain list, or a paged response, and may opt in
/// to caching via <see cref="ICacheable"/> on the query type.
/// </summary>
/// <remarks>
/// The feature set intentionally differs from the command-handler cohort. Queries don't
/// have ILogger or ICacheInvalidator dependencies in this codebase and don't load entities
/// via EF — so those features would be constant and add no signal. Instead the query
/// cohort tracks list-vs-single shape, pagination, cache opt-in, and SQL JOIN count —
/// the dimensions where query handlers actually vary.
/// </remarks>
public class QueryHandlerCohort : ICohortDefinition<QueryHandlerFingerprint>
{
    private static readonly Assembly ApiAssembly = typeof(StarterApp.Api.Infrastructure.IApiMarker).Assembly;

    public string CohortName => "QueryHandlers";

    /// <summary>
    /// Must match docs/exemplars/query-handlers/README.md.
    /// </summary>
    public IReadOnlyList<string> ExemplarTypeNames =>
    [
        "GetProductByIdQueryHandler",
        "GetAllProductsQueryHandler",
        "GetOrderByIdQueryHandler"
    ];

    public IReadOnlyList<Type> DiscoverTypes()
    {
        return ApiAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("QueryHandler"))
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .OrderBy(t => t.Name)
            .ToList();
    }

    public QueryHandlerFingerprint Extract(Type handlerType)
    {
        var ctor = handlerType.GetConstructors().FirstOrDefault();
        var ctorParams = ctor?.GetParameters() ?? [];

        var (queryType, responseType) = ResolveRequestHandlerTypes(handlerType);

        return new QueryHandlerFingerprint
        {
            TypeName = handlerType.Name,
            IlByteSize = IlInspector.SumIlByteSize(handlerType),
            ConstructorDependencyCount = ctorParams.Length,
            HasPagination = HasPaginationShape(queryType, responseType),
            IsCacheable = queryType is not null && typeof(ICacheable).IsAssignableFrom(queryType),
            ReturnsList = ReturnsListShape(responseType),
            JoinCount = IlInspector.CountSubstringInStringLiterals(handlerType, "JOIN")
                + IlInspector.CountSubstringInStringLiterals(handlerType, "APPLY"),
            SqlStatementCount = IlInspector.CountSubstringInStringLiterals(handlerType, "SELECT")
        };
    }

    private static (Type? Query, Type? Response) ResolveRequestHandlerTypes(Type handlerType)
    {
        var iface = handlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));

        if (iface is null)
            return (null, null);

        var args = iface.GetGenericArguments();
        return (args[0], args[1]);
    }

    private static bool IsPagedResponseType(Type? responseType) =>
        responseType is { IsGenericType: true } &&
        responseType.GetGenericTypeDefinition() == typeof(PagedResponse<>);

    private static bool HasPaginationShape(Type? queryType, Type? responseType)
    {
        if (IsPagedResponseType(responseType))
            return true;

        if (queryType is null)
            return false;

        return queryType.GetProperty("Page") is not null
            && queryType.GetProperty("PageSize") is not null;
    }

    private static bool ReturnsListShape(Type? responseType)
    {
        if (responseType is null)
            return false;

        if (IsPagedResponseType(responseType))
            return true;

        if (!responseType.IsGenericType)
            return false;

        var gtd = responseType.GetGenericTypeDefinition();
        return gtd == typeof(IReadOnlyList<>) || gtd == typeof(IReadOnlyCollection<>)
            || gtd == typeof(IEnumerable<>) || gtd == typeof(List<>);
    }
}
