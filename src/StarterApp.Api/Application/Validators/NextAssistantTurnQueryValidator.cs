namespace StarterApp.Api.Application.Validators;

public class NextAssistantTurnQueryValidator : IValidator<NextAssistantTurnQuery>
{
    public IEnumerable<ValidationError> Validate(NextAssistantTurnQuery request)
    {
        if (request.Area is not null && AssistantAreas.RolesOf(request.Area) is null)
            yield return new ValidationError(nameof(request.Area), "There is no such area.");

        var messages = request.Messages;
        if (messages.Count == 0)
        {
            yield return new ValidationError(nameof(request.Messages), "Say what you want done.");
            yield break;
        }

        if (messages.Count > NextAssistantTurnQuery.MaxMessages)
            yield return new ValidationError(nameof(request.Messages), $"A conversation with the assistant holds at most {NextAssistantTurnQuery.MaxMessages} messages; start a new one.");
        if (messages[0].Role != AssistantTurnMapper.User)
            yield return new ValidationError(nameof(request.Messages), "The conversation starts with what the person asked.");
        if (messages[^1].Role == AssistantTurnMapper.Assistant)
            yield return new ValidationError(nameof(request.Messages), "The conversation ends with the person's message or a tool's results.");

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case AssistantTurnMapper.User:
                    if (string.IsNullOrWhiteSpace(message.Text))
                        yield return new ValidationError(nameof(message.Text), "Say what you want done.");
                    else if (message.Text.Length > NextAssistantTurnQuery.MaxTextLength)
                        yield return new ValidationError(nameof(message.Text), $"A message to the assistant is at most {NextAssistantTurnQuery.MaxTextLength} characters.");
                    break;
                case AssistantTurnMapper.Assistant:
                    if (message.Blocks is not { Count: > 0 } || message.Blocks.Any(b => !AssistantTurnMapper.BlockTypes.Contains(b.Type)))
                        yield return new ValidationError(nameof(message.Blocks), "An assistant message carries the blocks the assistant returned.");
                    break;
                case AssistantTurnMapper.Tool:
                    if (message.Results is not { Count: > 0 } || message.Results.Any(r => string.IsNullOrWhiteSpace(r.ToolUseId)))
                        yield return new ValidationError(nameof(message.Results), "A tool message carries a result for each tool the assistant called.");
                    else if (message.Results.Any(r => (r.Content ?? string.Empty).Length > NextAssistantTurnQuery.MaxToolResultLength))
                        yield return new ValidationError(nameof(message.Results), $"A tool result is at most {NextAssistantTurnQuery.MaxToolResultLength} characters.");
                    break;
                default:
                    yield return new ValidationError(nameof(message.Role), "A message's role is user, assistant or tool.");
                    break;
            }
        }
    }
}
