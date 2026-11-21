using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure.Time;
using StarterApp.Api.Modules.Assistant.Application.DTOs;

namespace StarterApp.Api.Modules.Assistant.Application;

// One turn of the model. The browser holds the conversation and runs the find tools through the API as the person; nothing here reads or writes a record.
public sealed class AssistantTurns
{
    public const int MaxMessages = 80;
    public const int MaxTextLength = 2000;
    public const int MaxToolResultLength = 8000;

    private readonly IAssistantModel _model;
    private readonly AssistantCatalogue _catalogue;
    private readonly AssistantOptions _options;
    private readonly BusinessClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AssistantTurns> _logger;

    public AssistantTurns(IAssistantModel model, AssistantCatalogue catalogue, IOptions<AssistantOptions> options, BusinessClock clock, ICurrentUser currentUser, ILogger<AssistantTurns> logger)
    {
        _model = model;
        _catalogue = catalogue;
        _options = options.Value;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<AssistantTurnDto> NextAsync(string area, IReadOnlyList<AssistantMessageDto> messages, CancellationToken cancellationToken)
    {
        var errors = Validate(messages).ToList();
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            OutputConfig = new OutputConfig { Effort = AssistantTurnMapper.ToEffort(_options.Effort) },
            System = new List<TextBlockParam>
            {
                new() { Text = AssistantPrompt.SystemFor(area) },
                new() { Text = AssistantPrompt.NotesFor(area) },
                new() { Text = await _catalogue.TextAsync(area, cancellationToken), CacheControl = new CacheControlEphemeral() },
                new() { Text = AssistantPrompt.Today(_clock.Today) },
            },
            Tools = AssistantPrompt.ToolsFor(area).ToList(),
            Messages = messages.Select(AssistantTurnMapper.ToMessageParam).ToList(),
        };

        var message = await _model.CreateAsync(parameters, cancellationToken);
        var turn = AssistantTurnMapper.ToDto(message);
        foreach (var block in turn.Blocks.Where(block => block.Name is AssistantPrompt.Read or AssistantPrompt.Propose))
        {
            var method = block.Name == AssistantPrompt.Read ? "GET" : Text(block, "method");
            if (await _catalogue.FindAsync(area, method, Text(block, "path"), cancellationToken) is { } route)
                block.Route = AssistantTurnMapper.ToDto(route);
        }

        _logger.LogInformation(
            "Assistant turn for {Subject} in {Area}: model {Model}, stop {StopReason}, tools {Tools}, {InputTokens} in, {OutputTokens} out",
            _currentUser.Subject, area, _options.Model, turn.StopReason,
            string.Join(",", turn.Blocks.Where(b => b.Type == AssistantTurnMapper.ToolUse).Select(b => b.Name)),
            message.Usage.InputTokens, message.Usage.OutputTokens);

        return turn;
    }

    private static string? Text(AssistantBlockDto block, string name) =>
        block.Input is { } input && input.TryGetValue(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;

    internal static IEnumerable<ValidationError> Validate(IReadOnlyList<AssistantMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            yield return new ValidationError("Messages", "Say what you want done.");
            yield break;
        }

        if (messages.Count > MaxMessages)
            yield return new ValidationError("Messages", $"A conversation with the assistant holds at most {MaxMessages} messages; start a new one.");
        if (messages[0].Role != AssistantTurnMapper.User)
            yield return new ValidationError("Messages", "The conversation starts with what the person asked.");
        if (messages[^1].Role == AssistantTurnMapper.Assistant)
            yield return new ValidationError("Messages", "The conversation ends with the person's message or a tool's results.");

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case AssistantTurnMapper.User:
                    if (string.IsNullOrWhiteSpace(message.Text))
                        yield return new ValidationError(nameof(message.Text), "Say what you want done.");
                    else if (message.Text.Length > MaxTextLength)
                        yield return new ValidationError(nameof(message.Text), $"A message to the assistant is at most {MaxTextLength} characters.");
                    break;
                case AssistantTurnMapper.Assistant:
                    if (message.Blocks is not { Count: > 0 } || message.Blocks.Any(b => !AssistantTurnMapper.BlockTypes.Contains(b.Type)))
                        yield return new ValidationError(nameof(message.Blocks), "An assistant message carries the blocks the assistant returned.");
                    break;
                case AssistantTurnMapper.Tool:
                    if (message.Results is not { Count: > 0 } || message.Results.Any(r => string.IsNullOrWhiteSpace(r.ToolUseId)))
                        yield return new ValidationError(nameof(message.Results), "A tool message carries a result for each tool the assistant called.");
                    else if (message.Results.Any(r => (r.Content ?? string.Empty).Length > MaxToolResultLength))
                        yield return new ValidationError(nameof(message.Results), $"A tool result is at most {MaxToolResultLength} characters.");
                    break;
                default:
                    yield return new ValidationError(nameof(message.Role), "A message's role is user, assistant or tool.");
                    break;
            }
        }
    }
}
