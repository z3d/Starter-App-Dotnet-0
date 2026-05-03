namespace StarterApp.ServiceDefaults.Payloads;

public interface IPayloadArchiveStore
{
    Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken);
    Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
}
