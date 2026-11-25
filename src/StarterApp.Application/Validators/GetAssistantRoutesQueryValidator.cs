namespace StarterApp.Application.Validators;

public class GetAssistantRoutesQueryValidator : IValidator<GetAssistantRoutesQuery>
{
    public IEnumerable<ValidationError> Validate(GetAssistantRoutesQuery request)
    {
        if (request.Area is not null && AssistantAreas.ScopeOf(request.Area) is null)
            yield return new ValidationError(nameof(request.Area), "There is no such area.");
    }
}
