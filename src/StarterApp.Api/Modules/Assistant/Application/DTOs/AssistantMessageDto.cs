namespace StarterApp.Api.Modules.Assistant.Application.DTOs;

public class AssistantMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string? Text { get; set; }
    public List<AssistantBlockDto>? Blocks { get; set; }
    public List<AssistantToolResultDto>? Results { get; set; }
}

public class AssistantToolResultDto
{
    public string ToolUseId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; }
}
