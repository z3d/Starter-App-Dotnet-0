using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;
using System.Text;

namespace StarterApp.ServiceDefaults.Payloads;

public sealed class AzureBlobPayloadArchiveStore : IPayloadArchiveStore
{
    private readonly BlobContainerClient _containerClient;
    private readonly PayloadCaptureOptions _options;

    public AzureBlobPayloadArchiveStore(BlobServiceClient blobServiceClient, IOptions<PayloadCaptureOptions> options)
    {
        _options = options.Value;
        _containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
    }

    public async Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var appendBlobClient = _containerClient.GetAppendBlobClient(blobName);
        await appendBlobClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line + Environment.NewLine));
        await appendBlobClient.AppendBlockAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var archiveDeleted = await DeletePrefixOlderThanAsync(_options.ArchivePrefix, cutoffUtc, cancellationToken);
        var auditDeleted = await DeletePrefixOlderThanAsync(_options.AuditPrefix, cutoffUtc, cancellationToken);
        return new PayloadArchiveDeleteResult(archiveDeleted, auditDeleted);
    }

    private async Task<int> DeletePrefixOlderThanAsync(string prefix, DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var normalizedPrefix = prefix.Trim().Trim('/') + "/";

        await foreach (var blob in _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, normalizedPrefix, cancellationToken))
        {
            if (deleted >= _options.CleanupBatchSize)
                break;

            if (!PayloadBlobNaming.TryGetBlobMinute(blob.Name, out var blobMinuteUtc) || blobMinuteUtc >= cutoffUtc)
                continue;

            try
            {
                var response = await _containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
                if (response.Value)
                    deleted++;
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "BlobNotFound")
            {
                // A concurrent cleanup already removed it.
            }
        }

        return deleted;
    }
}
