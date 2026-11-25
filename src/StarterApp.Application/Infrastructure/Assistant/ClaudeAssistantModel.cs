using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace StarterApp.Application.Infrastructure.Assistant;

public sealed class ClaudeAssistantModel : IAssistantModel, IDisposable
{
    public const string HttpClientName = "assistant-model";

    private readonly AnthropicClient _client;

    public ClaudeAssistantModel(IHttpClientFactory httpClientFactory, IOptions<AssistantOptions> options)
    {
        _client = new AnthropicClient
        {
            ApiKey = options.Value.ApiKey,
            HttpClient = httpClientFactory.CreateClient(HttpClientName),
        };
    }

    public Task<Message> CreateAsync(MessageCreateParams parameters, CancellationToken cancellationToken) =>
        _client.Messages.Create(parameters, cancellationToken);

    public void Dispose() => _client.Dispose();
}
