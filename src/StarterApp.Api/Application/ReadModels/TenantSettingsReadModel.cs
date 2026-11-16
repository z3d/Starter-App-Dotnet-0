namespace StarterApp.Api.Application.ReadModels;

public class TenantSettingsReadModel
{
    public string? Palette { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? LastUpdated { get; set; }
}
