using System.Text;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class ArtifactCaptureSinkTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 6, 11, 9, 30, 0, TimeSpan.Zero);

    private static ArtifactCaptureSink CreateArtifactSink(IPayloadArchiveStore store, PayloadCaptureOptions? options = null)
    {
        var captureOptions = options ?? new PayloadCaptureOptions();
        return new ArtifactCaptureSink(
            PayloadCaptureTests.CreateSink(store, Timestamp, captureOptions),
            Microsoft.Extensions.Options.Options.Create(captureOptions));
    }

    [Fact]
    public async Task CaptureArtifact_WritesArchiveAuditAndEntityIndexBlobs()
    {
        var store = new InMemoryPayloadArchiveStore();
        var sink = CreateArtifactSink(store);
        using var correlationScope = CorrelationContext.Push("artifact-case-1");

        await sink.CaptureArtifactAsync(new ArtifactCaptureRequest
        {
            ArtifactName = "order-confirmation-document",
            Stage = ArtifactCaptureSink.GeneratedStage,
            ContentType = "application/json",
            Payload = """{"orderId":"00000000-0000-0000-0000-000000000001","total":42.00}""",
            EntityReferences = [new PayloadEntityReference("order", "00000000-0000-0000-0000-000000000001")]
        }, CancellationToken.None);

        var archive = store.Lines.Single(pair => pair.Key == "archive/2026-06-11/09/30/artifact-case-1.jsonl");
        var audit = store.Lines.Single(pair => pair.Key == "audit/2026-06-11/09/30/payload-audit.jsonl");
        var entityIndex = store.Lines.Single(pair => pair.Key.StartsWith("entity-index/order/", StringComparison.Ordinal));

        Assert.Contains("\"channel\":\"artifact\"", audit.Value.Single());
        Assert.Contains("\"artifact\":\"order-confirmation-document\"", audit.Value.Single());
        Assert.Contains("\"stage\":\"generated\"", audit.Value.Single());
        Assert.Contains("generated order-confirmation-document", audit.Value.Single());
        Assert.Single(archive.Value);
        Assert.Single(entityIndex.Value);
    }

    [Fact]
    public async Task CaptureBinaryArtifact_Base64EncodesAndMarksEncoding()
    {
        var store = new InMemoryPayloadArchiveStore();
        var sink = CreateArtifactSink(store);
        using var correlationScope = CorrelationContext.Push("artifact-case-2");
        var content = Encoding.UTF8.GetBytes("%PDF-1.7 fake document bytes");

        await sink.CaptureBinaryArtifactAsync(
            "invoice-pdf", ArtifactCaptureSink.GeneratedStage, "application/pdf", content, null, CancellationToken.None);

        var audit = store.Lines.Single(pair => pair.Key.StartsWith("audit/", StringComparison.Ordinal)).Value.Single();
        Assert.Contains("\"encoding\":\"base64\"", audit);
        Assert.Contains(Convert.ToBase64String(content), audit);
        Assert.Contains($"\"binarySizeBytes\":\"{content.Length}\"", audit);
    }

    [Fact]
    public async Task CaptureArtifact_OverThePayloadLimit_TruncatesAndSaysSo()
    {
        var store = new InMemoryPayloadArchiveStore();
        var options = new PayloadCaptureOptions { MaxPayloadBytes = 32 };
        var sink = CreateArtifactSink(store, options);
        using var correlationScope = CorrelationContext.Push("artifact-case-3");

        await sink.CaptureArtifactAsync(new ArtifactCaptureRequest
        {
            ArtifactName = "bulk-export",
            Stage = ArtifactCaptureSink.IntermediateStage,
            ContentType = "text/csv",
            Payload = new string('x', 500)
        }, CancellationToken.None);

        var audit = store.Lines.Single(pair => pair.Key.StartsWith("audit/", StringComparison.Ordinal)).Value.Single();
        Assert.Contains("\"payloadTruncated\":true", audit);
        Assert.Contains("exceeded configured limit", audit);
    }

    [Fact]
    public async Task CaptureArtifact_FailOpen_SwallowsStoreFailures()
    {
        var sink = CreateArtifactSink(new ThrowingPayloadArchiveStore(),
            new PayloadCaptureOptions { ArtifactFailureMode = PayloadCaptureFailureMode.FailOpen });

        await sink.CaptureArtifactAsync(new ArtifactCaptureRequest
        {
            ArtifactName = "doomed",
            Payload = "{}"
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CaptureArtifact_FailClosed_PropagatesStoreFailures()
    {
        var sink = CreateArtifactSink(new ThrowingPayloadArchiveStore(),
            new PayloadCaptureOptions { ArtifactFailureMode = PayloadCaptureFailureMode.FailClosed });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.CaptureArtifactAsync(new ArtifactCaptureRequest
        {
            ArtifactName = "doomed",
            Payload = "{}"
        }, CancellationToken.None));
    }

    private sealed class ThrowingPayloadArchiveStore : IPayloadArchiveStore
    {
        public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
            => throw new InvalidOperationException("archive store is down");

        public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
            => throw new InvalidOperationException("archive store is down");
    }
}
