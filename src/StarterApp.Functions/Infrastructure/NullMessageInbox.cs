namespace StarterApp.Functions;

// With no database the work just runs. Standalone runs and tests have no duplicates worth guarding against.
public sealed class NullMessageInbox : IMessageInbox
{
    public async Task<bool> ProcessOnceAsync(string consumer, string messageId, Func<InboxTransaction?, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await work(null, cancellationToken);
        return true;
    }
}
