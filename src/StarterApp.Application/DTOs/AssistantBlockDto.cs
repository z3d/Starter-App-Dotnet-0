using System.Text.Json;

namespace StarterApp.Application.DTOs;

public class AssistantBlockDto
{
    public string Type { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? Thinking { get; set; }
    public string? Signature { get; set; }
    public string? Data { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, JsonElement>? Input { get; set; }
    public AssistantRouteDto? Route { get; set; }
}
