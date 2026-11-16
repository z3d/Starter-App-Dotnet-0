namespace StarterApp.Api.Application.Validators;

public class GetTenantSettingsQueryValidator : IValidator<GetTenantSettingsQuery>
{
    public IEnumerable<ValidationError> Validate(GetTenantSettingsQuery request)
    {
        yield break;
    }
}
