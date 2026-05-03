using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;
using System.Text.Json;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class PayloadCaptureTests
{
    [Fact]
    public void BuildArchiveBlobName_ShouldUseDateHourMinuteAndCorrelation()
    {
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 59, TimeSpan.Zero);

        var blobName = PayloadBlobNaming.BuildArchiveBlobName(timestamp, "case-123");

        Assert.Equal("archive/2026-05-03/04/07/case-123.jsonl", blobName);
    }

    [Fact]
    public void BuildEntityIndexBlobName_ShouldUseEntityDateHourMinuteAndCorrelation()
    {
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 59, TimeSpan.Zero);

        var blobName = PayloadBlobNaming.BuildEntityIndexBlobName(timestamp, new PayloadEntityReference("customer", "42"), "case-123");

        Assert.Equal("entity-index/customer/42/2026-05-03/04/07/case-123.jsonl", blobName);
    }

    [Fact]
    public void JsonPayloadRedactor_ShouldMaskSensitiveJsonFieldsAndKeepOtherValues()
    {
        var redactor = new JsonPayloadRedactor(Options.Create(new PayloadCaptureOptions()));

        var redacted = redactor.Redact("""{"name":"Ada","email":"ada@example.com","total":42}""", "application/json");

        Assert.Contains("***REDACTED***", redacted);
        Assert.DoesNotContain("ada@example.com", redacted);
        Assert.Contains("\"total\":42", redacted);
    }

    [Fact]
    public async Task CaptureAsync_ShouldWriteFullPayloadToArchiveAndAuditWithCorrelationAndTime()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(store, timestamp);

        await sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "inbound",
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"name":"Ada","email":"ada@example.com","total":42}"""
        }, CancellationToken.None);

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-123.jsonl");
        var auditEntry = store.Lines.Single(pair => pair.Key == "audit/2026-05-03/04/07/payload-audit.jsonl");

        using var archiveJson = JsonDocument.Parse(archiveEntry.Value.Single());
        using var auditJson = JsonDocument.Parse(auditEntry.Value.Single());

        Assert.Equal("case-123", archiveJson.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("2026-05-03T04:07:00+00:00", archiveJson.RootElement.GetProperty("timestampUtc").GetString());
        Assert.Contains("ada@example.com", archiveJson.RootElement.GetProperty("payload").GetString());
        Assert.Equal(archiveJson.RootElement.GetProperty("archiveBlobName").GetString(), auditJson.RootElement.GetProperty("archiveBlobName").GetString());
    }

    [Fact]
    public async Task CaptureAsync_ShouldWriteEntityIndexWithPointersOnly()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(store, timestamp);

        await sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "outbound",
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"id":42,"name":"Ada","email":"ada@example.com","total":42}""",
            Metadata = new Dictionary<string, string>
            {
                ["path"] = "/api/v1/customers",
                ["queryString"] = "?email=ada@example.com"
            }
        }, CancellationToken.None);

        var entityIndexEntry = store.Lines.Single(pair => pair.Key == "entity-index/customer/42/2026-05-03/04/07/case-123.jsonl");
        var entityIndexLine = entityIndexEntry.Value.Single();

        using var indexJson = JsonDocument.Parse(entityIndexLine);
        Assert.Equal("customer", indexJson.RootElement.GetProperty("entityType").GetString());
        Assert.Equal("42", indexJson.RootElement.GetProperty("entityId").GetString());
        Assert.Equal("archive/2026-05-03/04/07/case-123.jsonl", indexJson.RootElement.GetProperty("archiveBlobName").GetString());
        Assert.Equal("audit/2026-05-03/04/07/payload-audit.jsonl", indexJson.RootElement.GetProperty("auditBlobName").GetString());
        Assert.False(indexJson.RootElement.TryGetProperty("payload", out _));
        Assert.DoesNotContain("ada@example.com", entityIndexLine);

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-123.jsonl");
        using var archiveJson = JsonDocument.Parse(archiveEntry.Value.Single());
        var entityReference = archiveJson.RootElement.GetProperty("entityReferences").EnumerateArray().Single();
        Assert.Equal("customer", entityReference.GetProperty("entityType").GetString());
        Assert.Equal("42", entityReference.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task DeleteOlderThanAsync_ShouldDeleteArchiveAuditAndEntityIndexBlobsByPathTime()
    {
        var store = new InMemoryPayloadArchiveStore();
        await store.AppendLineAsync("archive/2026-05-01/01/00/case-old.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("audit/2026-05-01/01/00/payload-audit.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("entity-index/customer/42/2026-05-01/01/00/case-old.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("archive/2026-05-09/01/00/case-new.jsonl", "{}", CancellationToken.None);

        var result = await store.DeleteOlderThanAsync(new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(1, result.ArchiveDeleted);
        Assert.Equal(1, result.AuditDeleted);
        Assert.Equal(1, result.EntityIndexDeleted);
        Assert.Contains("archive/2026-05-09/01/00/case-new.jsonl", store.Lines.Keys);
    }

    internal static PayloadCaptureSink CreateSink(InMemoryPayloadArchiveStore store, DateTimeOffset timestamp)
    {
        var options = Options.Create(new PayloadCaptureOptions());
        var logger = new LoggerFactory().CreateLogger<PayloadCaptureSink>();
        return new PayloadCaptureSink(store, new JsonPayloadRedactor(options), new FixedTimeProvider(timestamp), options, logger);
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _timestamp;

        public FixedTimeProvider(DateTimeOffset timestamp)
        {
            _timestamp = timestamp;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _timestamp;
        }
    }
}
