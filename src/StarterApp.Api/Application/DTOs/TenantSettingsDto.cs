namespace StarterApp.Api.Application.DTOs;

public class TenantSettingsDto
{
    public string Palette { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
}
