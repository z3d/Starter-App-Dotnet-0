namespace StarterApp.ServiceDefaults.Payloads;

public sealed class NullPayloadArchiveStore : IPayloadArchiveStore
{
    public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        return Task.FromResult(new PayloadArchiveDeleteResult(0, 0));
    }
}
