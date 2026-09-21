using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class AzureBlobChunkingTests
{
    [Fact]
    public void ChunkBlocks_WhenWithinBlockLimit_ShouldYieldSingleChunk()
    {
        var bytes = new byte[1000];

        var chunks = AzureBlobPayloadArchiveStore.ChunkBlocks(bytes, AzureBlobPayloadArchiveStore.MaxAppendBlockBytes).ToList();

        Assert.Single(chunks);
        Assert.Equal(bytes.Length, chunks[0].Count);
    }

    [Fact]
    public void ChunkBlocks_WhenExceedingBlockLimit_ShouldSplitIntoOrderedChunksNoneOverLimit()
    {
        const int maxBlock = 4 * 1024 * 1024;
        var bytes = new byte[(maxBlock * 2) + 123];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 251);

        var chunks = AzureBlobPayloadArchiveStore.ChunkBlocks(bytes, maxBlock).ToList();

        // Azure Append Blob rejects any single block larger than 4 MiB.
        Assert.All(chunks, chunk => Assert.True(chunk.Count <= maxBlock));
        Assert.Equal(3, chunks.Count);

        // Concatenation must reproduce the original line exactly (Append Blob preserves append order).
        var reassembled = chunks.SelectMany(chunk => chunk).ToArray();
        Assert.Equal(bytes, reassembled);
    }

    [Fact]
    public void BuildOversizeSidecarBlobName_ShouldStayUniqueAndKeepRetentionMinutePath()
    {
        // Oversize records go to a single-writer sidecar so multi-block appends cannot interleave
        // with concurrent writers in the shared per-minute audit blob. The sidecar must keep the
        // parent's date/hour/minute segments so retention cleanup still covers it.
        const string parent = "audit/2026-06-10/09/41/payload-audit.jsonl";

        var first = AzureBlobPayloadArchiveStore.BuildOversizeSidecarBlobName(parent);
        var second = AzureBlobPayloadArchiveStore.BuildOversizeSidecarBlobName(parent);

        Assert.StartsWith(parent + ".oversize-", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);

        Assert.True(PayloadBlobNaming.TryGetBlobMinute(first, out var sidecarMinute));
        Assert.True(PayloadBlobNaming.TryGetBlobMinute(parent, out var parentMinute));
        Assert.Equal(parentMinute, sidecarMinute);
    }

    [Fact]
    public void BuildOversizePointerLine_ShouldBeSmallSingleLineJsonReferencingSidecar()
    {
        const string sidecar = "audit/2026-06-10/09/41/payload-audit.jsonl.oversize-abc.jsonl";

        var pointer = AzureBlobPayloadArchiveStore.BuildOversizePointerLine(sidecar, 9_000_000);

        // The pointer replaces the record in the shared blob, so it must itself be an atomic
        // single-block, single-line append.
        Assert.DoesNotContain('\n', pointer);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(pointer) < AzureBlobPayloadArchiveStore.MaxAppendBlockBytes);

        using var document = System.Text.Json.JsonDocument.Parse(pointer);
        Assert.True(document.RootElement.GetProperty("oversizeRecord").GetBoolean());
        Assert.Equal(sidecar, document.RootElement.GetProperty("sidecarBlobName").GetString());
        Assert.Equal(9_000_000, document.RootElement.GetProperty("recordSizeBytes").GetInt64());
    }
}
