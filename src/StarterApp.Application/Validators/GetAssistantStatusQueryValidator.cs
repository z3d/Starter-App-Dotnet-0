namespace StarterApp.Application.Validators;

public class GetAssistantStatusQueryValidator : IValidator<GetAssistantStatusQuery>
{
    public IEnumerable<ValidationError> Validate(GetAssistantStatusQuery request)
    {
        yield break;
    }
}
