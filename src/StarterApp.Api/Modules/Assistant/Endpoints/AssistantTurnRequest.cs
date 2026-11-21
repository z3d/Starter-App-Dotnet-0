using StarterApp.Api.Modules.Assistant.Application.DTOs;

namespace StarterApp.Api.Modules.Assistant.Endpoints;

public record AssistantTurnRequest(List<AssistantMessageDto> Messages, string? Area = null);
