namespace StarterApp.Functions;

public interface IMessageInbox
{
    // Returns false when this consumer has already handled the message. Service Bus delivers at least once, and out of order.
    Task<bool> ProcessOnceAsync(string consumer, string messageId, Func<InboxTransaction?, CancellationToken, Task> work, CancellationToken cancellationToken);
}
