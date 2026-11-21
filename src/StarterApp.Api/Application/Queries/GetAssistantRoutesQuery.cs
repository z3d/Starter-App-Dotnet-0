namespace StarterApp.Api.Application.Queries;

public class GetAssistantRoutesQuery : IQuery<AssistantRoutesDto>, IOwnerScopedRequest
{
    public string? Area { get; set; }
}

public class GetAssistantRoutesQueryHandler : IRequestHandler<GetAssistantRoutesQuery, AssistantRoutesDto>
{
    private readonly AssistantCatalogue _catalogue;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ICurrentUser _currentUser;

    public GetAssistantRoutesQueryHandler(AssistantCatalogue catalogue, IOwnerOnlyPolicy ownerOnlyPolicy, ICurrentUser currentUser)
    {
        _catalogue = catalogue;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _currentUser = currentUser;
    }

    public async Task<AssistantRoutesDto> HandleAsync(GetAssistantRoutesQuery query, CancellationToken cancellationToken)
    {
        _ownerOnlyPolicy.GetRequiredScope();
        AssistantAreas.EnsureAdmitted(query.Area, _currentUser);
        var routes = await _catalogue.RoutesForAsync(_currentUser.Scopes, cancellationToken);
        return new AssistantRoutesDto { Routes = routes.Where(route => !route.IsRead).Select(AssistantTurnMapper.ToDto).ToList() };
    }
}
