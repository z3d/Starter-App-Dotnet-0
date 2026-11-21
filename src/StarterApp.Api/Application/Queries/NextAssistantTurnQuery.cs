using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace StarterApp.Api.Application.Queries;

public class NextAssistantTurnQuery : IQuery<AssistantTurnDto>, IOwnerScopedRequest
{
    public const int MaxMessages = 80;
    public const int MaxTextLength = 2000;
    public const int MaxToolResultLength = 8000;

    public List<AssistantMessageDto> Messages { get; set; } = [];
    public string? Area { get; set; }
}

// One turn of the model. The browser holds the conversation and runs the find tools through the API as the person; nothing here reads or writes a record.
public class NextAssistantTurnQueryHandler : IRequestHandler<NextAssistantTurnQuery, AssistantTurnDto>
{
    private readonly IAssistantModel _model;
    private readonly AssistantCatalogue _catalogue;
    private readonly AssistantOptions _options;
    private readonly TimeProvider _time;
    private readonly IOwnerOnlyPolicy _ownerOnlyPolicy;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<NextAssistantTurnQueryHandler> _logger;

    public NextAssistantTurnQueryHandler(IAssistantModel model, AssistantCatalogue catalogue, IOptions<AssistantOptions> options, TimeProvider time, IOwnerOnlyPolicy ownerOnlyPolicy, ICurrentUser currentUser, ILogger<NextAssistantTurnQueryHandler> logger)
    {
        _model = model;
        _catalogue = catalogue;
        _options = options.Value;
        _time = time;
        _ownerOnlyPolicy = ownerOnlyPolicy;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<AssistantTurnDto> HandleAsync(NextAssistantTurnQuery query, CancellationToken cancellationToken)
    {
        var owner = _ownerOnlyPolicy.GetRequiredScope();
        var area = query.Area ?? AssistantAreas.App;
        AssistantAreas.EnsureAdmitted(area, _currentUser);

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            OutputConfig = new OutputConfig { Effort = AssistantTurnMapper.ToEffort(_options.Effort) },
            System = new List<TextBlockParam>
            {
                new() { Text = AssistantPrompt.SystemFor(area) },
                new() { Text = AssistantPrompt.NotesFor(area) },
                new() { Text = await _catalogue.TextAsync(_currentUser.Scopes, cancellationToken), CacheControl = new CacheControlEphemeral() },
                new() { Text = AssistantPrompt.Today(DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime)) },
            },
            Tools = AssistantPrompt.ToolsFor(area).ToList(),
            Messages = query.Messages.Select(AssistantTurnMapper.ToMessageParam).ToList(),
        };

        var message = await _model.CreateAsync(parameters, cancellationToken);
        var turn = AssistantTurnMapper.ToDto(message);
        foreach (var block in turn.Blocks.Where(block => block.Name is AssistantPrompt.Read or AssistantPrompt.Propose))
        {
            var method = block.Name == AssistantPrompt.Read ? "GET" : Text(block, "method");
            if (await _catalogue.FindAsync(_currentUser.Scopes, method, Text(block, "path"), cancellationToken) is { } route)
                block.Route = AssistantTurnMapper.ToDto(route);
        }

        _logger.LogInformation(
            "Assistant turn for {Subject} in {Area}: model {Model}, stop {StopReason}, tools {Tools}, {InputTokens} in, {OutputTokens} out",
            owner.OwnerSubject, area, _options.Model, turn.StopReason,
            string.Join(",", turn.Blocks.Where(b => b.Type == AssistantTurnMapper.ToolUse).Select(b => b.Name)),
            message.Usage.InputTokens, message.Usage.OutputTokens);

        return turn;
    }

    private static string? Text(AssistantBlockDto block, string name) =>
        block.Input is { } input && input.TryGetValue(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
}
