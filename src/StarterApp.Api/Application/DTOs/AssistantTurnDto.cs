namespace StarterApp.Api.Application.DTOs;

public class AssistantTurnDto
{
    public List<AssistantBlockDto> Blocks { get; set; } = [];
    public string StopReason { get; set; } = string.Empty;
}
