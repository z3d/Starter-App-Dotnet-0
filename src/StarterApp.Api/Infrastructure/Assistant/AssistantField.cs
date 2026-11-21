namespace StarterApp.Api.Infrastructure.Assistant;

public sealed record AssistantField(string Name, bool Required, string Kind, IReadOnlyList<string> Options, string? Label = null, string? WhenField = null, IReadOnlyList<string>? WhenValues = null);
