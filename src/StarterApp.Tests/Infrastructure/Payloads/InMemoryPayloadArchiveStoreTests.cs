using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class InMemoryPayloadArchiveStoreTests
{
    // The in-memory double backs every cleanup test, so its delete semantics must mirror
    // AzureBlobPayloadArchiveStore: configured prefixes only, per-prefix batch cap, and no
    // deletion outside the three payload prefixes.
    [Fact]
    public async Task DeleteOlderThan_ShouldHonorConfiguredPrefixes()
    {
        var options = new PayloadCaptureOptions { ArchivePrefix = "custom-archive", AuditPrefix = "custom-audit", EntityIndexPrefix = "custom-index" };
        var store = new InMemoryPayloadArchiveStore(options);
        await store.AppendLineAsync("custom-archive/2020-01-01/00/00/old.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("custom-audit/2020-01-01/00/00/payload-audit.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("custom-index/customer/1/2020-01-01/00/00/old.jsonl", "{}", CancellationToken.None);

        var result = await store.DeleteOlderThanAsync(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(1, result.ArchiveDeleted);
        Assert.Equal(1, result.AuditDeleted);
        Assert.Equal(1, result.EntityIndexDeleted);
        Assert.Empty(store.Lines);
    }

    [Fact]
    public async Task DeleteOlderThan_ShouldNotDeleteBlobsOutsidePayloadPrefixes()
    {
        var store = new InMemoryPayloadArchiveStore();
        await store.AppendLineAsync("unrelated/2020-01-01/00/00/data.jsonl", "{}", CancellationToken.None);

        var result = await store.DeleteOlderThanAsync(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(0, result.TotalDeleted);
        Assert.Single(store.Lines);
    }

    [Fact]
    public async Task DeleteOlderThan_ShouldCapDeletionsPerPrefixAtCleanupBatchSize()
    {
        var options = new PayloadCaptureOptions { CleanupBatchSize = 2 };
        var store = new InMemoryPayloadArchiveStore(options);
        for (var i = 0; i < 5; i++)
            await store.AppendLineAsync($"archive/2020-01-01/00/0{i}/case.jsonl", "{}", CancellationToken.None);

        var result = await store.DeleteOlderThanAsync(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(2, result.ArchiveDeleted);
        Assert.Equal(3, store.Lines.Count);
    }
}
