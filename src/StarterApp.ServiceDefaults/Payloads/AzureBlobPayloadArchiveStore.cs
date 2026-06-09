using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;

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

    // Azure Append Blob rejects any single AppendBlock larger than 4 MiB. MaxPayloadBytes can be
    // configured up to 100 MiB, so a captured line can exceed one block; split it into ordered chunks
    // (Append Blob preserves append order, so the JSONL line stays intact across blocks).
    internal const int MaxAppendBlockBytes = 4 * 1024 * 1024;

    public async Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var appendBlobClient = _containerClient.GetAppendBlobClient(blobName);
        await appendBlobClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        foreach (var block in ChunkBlocks(bytes, MaxAppendBlockBytes))
        {
            await using var stream = new MemoryStream(block.Array!, block.Offset, block.Count, writable: false);
            await appendBlobClient.AppendBlockAsync(stream, cancellationToken: cancellationToken);
        }
    }

    internal static IEnumerable<ArraySegment<byte>> ChunkBlocks(byte[] bytes, int maxBlock)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBlock);

        for (var offset = 0; offset < bytes.Length; offset += maxBlock)
        {
            var count = Math.Min(maxBlock, bytes.Length - offset);
            yield return new ArraySegment<byte>(bytes, offset, count);
        }
    }

    public async Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var archiveDeleted = await DeletePrefixOlderThanAsync(_options.ArchivePrefix, cutoffUtc, cancellationToken);
        var auditDeleted = await DeletePrefixOlderThanAsync(_options.AuditPrefix, cutoffUtc, cancellationToken);
        var entityIndexDeleted = await DeletePrefixOlderThanAsync(_options.EntityIndexPrefix, cutoffUtc, cancellationToken);
        return new PayloadArchiveDeleteResult(archiveDeleted, auditDeleted, entityIndexDeleted);
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
