using Microsoft.Extensions.Options;

namespace StarterApp.Application.Queries;

public class GetAssistantStatusQuery : IQuery<AssistantStatusDto>, IOwnerScopedRequest
{
}

public class GetAssistantStatusQueryHandler : IRequestHandler<GetAssistantStatusQuery, AssistantStatusDto>
{
    private readonly AssistantOptions _options;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;

    public GetAssistantStatusQueryHandler(IOptions<AssistantOptions> options, IOwnerOnlyPolicy ownerOnlyPolicy)
    {
        _options = options.Value;
        _ownerOnlyPolicy = ownerOnlyPolicy;
    }

    public Task<AssistantStatusDto> HandleAsync(GetAssistantStatusQuery query, CancellationToken cancellationToken)
    {
        _ownerOnlyPolicy.GetRequiredScope();
        return Task.FromResult(new AssistantStatusDto { Enabled = _options.IsConfigured, Primary = _options.IsConfigured && _options.PrimaryInterface });
    }
}
