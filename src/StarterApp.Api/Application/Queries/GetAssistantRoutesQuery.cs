namespace StarterApp.Api.Application.Queries;

public class GetAssistantRoutesQuery : IQuery<AssistantRoutesDto>, IOwnerScopedRequest
{
    public string? Area { get; set; }
}

public class GetAssistantRoutesQueryHandler : IRequestHandler<GetAssistantRoutesQuery, AssistantRoutesDto>
{
    private readonly AssistantCatalogue _catalogue;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetAssistantRoutesQueryHandler(AssistantCatalogue catalogue, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _catalogue = catalogue;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public async Task<AssistantRoutesDto> HandleAsync(GetAssistantRoutesQuery query, CancellationToken cancellationToken)
    {
        _ownerOnlyPolicy.GetRequiredScope();
        var routes = await _catalogue.RoutesForAsync(query.Area ?? AssistantAreas.Staff, cancellationToken);
        return new AssistantRoutesDto { Routes = routes.Where(route => !route.IsRead).Select(AssistantTurnMapper.ToDto).ToList() };
    }
}
