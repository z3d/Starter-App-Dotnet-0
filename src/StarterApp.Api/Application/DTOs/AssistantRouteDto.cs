namespace StarterApp.Api.Application.DTOs;

public class AssistantRouteDto
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public List<AssistantFieldDto> Fields { get; set; } = [];
}

public class AssistantFieldDto
{
    public string Name { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string Kind { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    public string? Label { get; set; }
    public string? WhenField { get; set; }
    public List<string>? WhenValues { get; set; }
}
