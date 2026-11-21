using Anthropic.Models.Messages;

namespace StarterApp.Api.Modules.Assistant.Application;

public interface IAssistantModel
{
    Task<Message> CreateAsync(MessageCreateParams parameters, CancellationToken cancellationToken);
}
