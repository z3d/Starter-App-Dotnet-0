using Anthropic.Models.Messages;

namespace StarterApp.Application.Infrastructure.Assistant;

public interface IAssistantModel
{
    Task<Message> CreateAsync(MessageCreateParams parameters, CancellationToken cancellationToken);
}
