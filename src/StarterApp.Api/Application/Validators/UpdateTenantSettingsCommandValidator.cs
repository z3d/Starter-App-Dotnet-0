namespace StarterApp.Api.Application.Validators;

public class UpdateTenantSettingsCommandValidator : IValidator<UpdateTenantSettingsCommand>
{
    public IEnumerable<ValidationError> Validate(UpdateTenantSettingsCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Palette))
            yield return new ValidationError(nameof(request.Palette), "Palette is required");
        else if (!TenantSettings.IsPaletteName(request.Palette))
            yield return new ValidationError(nameof(request.Palette),
                $"Palette must be a lower-case name of letters, digits and hyphens, at most {TenantSettings.MaxPaletteLength} characters");
    }
}
