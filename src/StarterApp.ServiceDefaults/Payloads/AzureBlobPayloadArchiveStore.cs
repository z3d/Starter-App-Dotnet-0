using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
    // configured up to 100 MiB, so a captured line can exceed one block.
    internal const int MaxAppendBlockBytes = 4 * 1024 * 1024;

    public async Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        if (bytes.Length <= MaxAppendBlockBytes)
        {
            // A single AppendBlock is atomic, so concurrent writers to a shared blob (the
            // per-minute audit stream) can never interleave inside one record.
            await AppendBlocksAsync(blobName, bytes, cancellationToken);
            return;
        }

        // Multi-block appends are NOT atomic: with no append-position guarantee across blocks,
        // a concurrent writer's record can land between two chunks of this line and splice two
        // records. Write the oversize record to a dedicated single-writer sidecar blob (chunked
        // appends are safe with one writer) and append a small, atomic pointer line to the shared
        // blob. Sidecar first, so a pointer can never dangle.
        var sidecarBlobName = BuildOversizeSidecarBlobName(blobName);
        await AppendBlocksAsync(sidecarBlobName, bytes, cancellationToken);

        var pointerBytes = Encoding.UTF8.GetBytes(BuildOversizePointerLine(sidecarBlobName, bytes.Length) + Environment.NewLine);
        await AppendBlocksAsync(blobName, pointerBytes, cancellationToken);
    }

    private async Task AppendBlocksAsync(string blobName, byte[] bytes, CancellationToken cancellationToken)
    {
        var appendBlobClient = _containerClient.GetAppendBlobClient(blobName);
        await appendBlobClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        foreach (var block in ChunkBlocks(bytes, MaxAppendBlockBytes))
        {
            await using var stream = new MemoryStream(block.Array!, block.Offset, block.Count, writable: false);
            await appendBlobClient.AppendBlockAsync(stream, cancellationToken: cancellationToken);
        }
    }

    // The sidecar keeps the date/hour/minute path segments of its parent, so retention cleanup's
    // minute parsing covers it; the unique suffix makes it single-writer by construction.
    internal static string BuildOversizeSidecarBlobName(string blobName)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{blobName}.oversize-{Guid.CreateVersion7():N}.jsonl");
    }

    private static readonly JsonSerializerOptions PointerSerializerOptions = new(JsonSerializerDefaults.Web);

    internal static string BuildOversizePointerLine(string sidecarBlobName, long recordSizeBytes)
    {
        return JsonSerializer.Serialize(new OversizePointer(true, sidecarBlobName, recordSizeBytes), PointerSerializerOptions);
    }

    internal sealed record OversizePointer(bool OversizeRecord, string SidecarBlobName, long RecordSizeBytes);

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

    // One listing pass per prefix, deleting expired blobs as they are encountered. Listing is
    // lexicographic and continuation markers are name-based, so deleting already-listed blobs
    // mid-enumeration is safe, and the cost is O(blobs under the prefix) per run. The wall-clock
    // budget is split evenly across the three prefixes so a large archive/ backlog cannot starve
    // entity-index/, and checked inline so a run never overshoots by more than one delete.
    public async Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var perPrefixBudget = TimeSpan.FromSeconds(_options.CleanupTimeBudgetSeconds) / 3;
        var archive = await DeletePrefixOlderThanAsync(_options.ArchivePrefix, cutoffUtc, perPrefixBudget, cancellationToken);
        var audit = await DeletePrefixOlderThanAsync(_options.AuditPrefix, cutoffUtc, perPrefixBudget, cancellationToken);
        var entityIndex = await DeletePrefixOlderThanAsync(_options.EntityIndexPrefix, cutoffUtc, perPrefixBudget, cancellationToken);
        return new PayloadArchiveDeleteResult(
            archive.Deleted, audit.Deleted, entityIndex.Deleted,
            archive.BudgetExhausted || audit.BudgetExhausted || entityIndex.BudgetExhausted);
    }

    private async Task<(int Deleted, bool BudgetExhausted)> DeletePrefixOlderThanAsync(string prefix, DateTimeOffset cutoffUtc, TimeSpan budget, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var started = Stopwatch.GetTimestamp();
        var normalizedPrefix = prefix.Trim().Trim('/') + "/";

        await foreach (var blob in _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, normalizedPrefix, cancellationToken))
        {
            if (Stopwatch.GetElapsedTime(started) >= budget)
                return (deleted, true);

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

        return (deleted, false);
    }
}
