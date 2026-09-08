namespace StarterApp.Api.Endpoints;

public static class EndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var endpointDefinitions = typeof(IApiMarker).Assembly
            .GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IEndpointDefinition)) &&
                       !t.IsAbstract &&
                       !t.IsInterface)
            .Select(Activator.CreateInstance)
            .Cast<IEndpointDefinition>();

        foreach (var definition in endpointDefinitions)
        {
            definition.DefineEndpoints(app);
        }

        return app;
    }

    // List queries fetch pageSize + 1 rows; the extra row is the "another page exists" probe.
    public static async Task<IResult> PagedAsync<T>(this IMediator mediator, IRequest<IEnumerable<T>> query, int pageSize, CancellationToken cancellationToken)
    {
        var items = (await mediator.SendAsync(query, cancellationToken)).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        return Results.Ok(new PagedResponse<T> { Data = items, HasMore = hasMore });
    }
}
