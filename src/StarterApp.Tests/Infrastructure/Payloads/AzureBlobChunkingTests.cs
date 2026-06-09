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
}
