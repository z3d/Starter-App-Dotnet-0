using Anthropic.Models.Messages;

namespace StarterApp.Api.Infrastructure.Assistant;

public static class AssistantTurnMapper
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";

    public const string Text = "text";
    public const string Thinking = "thinking";
    public const string RedactedThinking = "redacted_thinking";
    public const string ToolUse = "tool_use";

    public static readonly IReadOnlySet<string> BlockTypes = new HashSet<string>(StringComparer.Ordinal) { Text, Thinking, RedactedThinking, ToolUse };

    public static Effort ToEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        _ => Effort.Low,
    };

    public static MessageParam ToMessageParam(AssistantMessageDto message) => message.Role switch
    {
        User => new UserMessageParam { Content = message.Text ?? string.Empty },
        Assistant => new AssistantMessageParam { Content = (message.Blocks ?? []).Select(ToBlockParam).ToList() },
        _ => new UserMessageParam
        {
            Content = (message.Results ?? [])
                .Select(result => (ContentBlockParam)new ToolResultBlockParam { ToolUseID = result.ToolUseId, Content = result.Content ?? string.Empty, IsError = result.IsError })
                .ToList(),
        },
    };

    public static AssistantRouteDto ToDto(AssistantRoute route) => new()
    {
        Method = route.Method,
        Path = route.Path,
        Summary = route.Summary,
        Scope = route.Scope,
        Fields = route.BodyFields.Select(field => new AssistantFieldDto { Name = field.Name, Required = field.Required, Kind = field.Kind, Options = field.Options.ToList(), Label = field.Label, WhenField = field.WhenField, WhenValues = field.WhenValues?.ToList() }).ToList(),
    };

    public static AssistantTurnDto ToDto(Message message)
    {
        var blocks = new List<AssistantBlockDto>();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text))
                blocks.Add(new AssistantBlockDto { Type = Text, Text = text.Text });
            else if (block.TryPickThinking(out var thinking))
                blocks.Add(new AssistantBlockDto { Type = Thinking, Thinking = thinking.Thinking, Signature = thinking.Signature });
            else if (block.TryPickRedactedThinking(out var redacted))
                blocks.Add(new AssistantBlockDto { Type = RedactedThinking, Data = redacted.Data });
            else if (block.TryPickToolUse(out var toolUse))
                blocks.Add(new AssistantBlockDto { Type = ToolUse, Id = toolUse.ID, Name = toolUse.Name, Input = new(toolUse.Input) });
        }

        return new AssistantTurnDto { Blocks = blocks, StopReason = message.StopReason?.Raw() ?? string.Empty };
    }

    private static ContentBlockParam ToBlockParam(AssistantBlockDto block) => block.Type switch
    {
        Thinking => new ThinkingBlockParam { Thinking = block.Thinking ?? string.Empty, Signature = block.Signature ?? string.Empty },
        RedactedThinking => new RedactedThinkingBlockParam { Data = block.Data ?? string.Empty },
        ToolUse => new ToolUseBlockParam { ID = block.Id ?? string.Empty, Name = block.Name ?? string.Empty, Input = block.Input ?? [] },
        _ => new TextBlockParam { Text = block.Text ?? string.Empty },
    };
}
