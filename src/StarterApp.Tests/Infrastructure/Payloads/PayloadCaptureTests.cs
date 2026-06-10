using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

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
    public void Extract_WithManyDistinctIdProperties_ShouldCapReferencesAndFlagTruncation()
    {
        // A hostile body packed with thousands of distinct *Id properties must not fan out into thousands
        // of serial blob round trips: the extractor caps the reference count and signals truncation.
        var properties = Enumerable.Range(0, 10_000).Select(i => $"\"a{i}Id\":{i}");
        var payload = "{" + string.Join(",", properties) + "}";
        var request = new PayloadCaptureRequest
        {
            Operation = "POST /api/v1/orders",
            Channel = "http",
            ContentType = "application/json",
            Payload = payload
        };

        var references = PayloadEntityReferenceExtractor.Extract(request, 64, out var truncated);

        Assert.True(truncated);
        Assert.Equal(64, references.Count);
    }

    [Fact]
    public void Extract_WithFewReferences_ShouldNotTruncate()
    {
        var request = new PayloadCaptureRequest
        {
            Operation = "POST /api/v1/orders",
            Channel = "http",
            ContentType = "application/json",
            Payload = """{"customerId":42,"productId":7}"""
        };

        var references = PayloadEntityReferenceExtractor.Extract(request, 64, out var truncated);

        Assert.False(truncated);
        Assert.True(references.Count <= 64);
    }

    [Fact]
    public void Extract_WithSensitivePropertyNames_ShouldNotEmitThemAsEntityReferences()
    {
        // Entity references become blob path segments and are echoed verbatim in Information logs,
        // so *Id properties matching SensitivePropertyNames must never become references.
        var request = new PayloadCaptureRequest
        {
            Operation = "POST /api/v1/customers",
            Channel = "http",
            ContentType = "application/json",
            Payload = """{"nationalId":"123-45-6789","ssnId":"987654321","passportId":"PA1234567","customerId":42}"""
        };

        var references = PayloadEntityReferenceExtractor.Extract(request, 64, out _);

        Assert.DoesNotContain(references, reference => reference.EntityType == "national");
        Assert.DoesNotContain(references, reference => reference.EntityType == "ssn");
        Assert.DoesNotContain(references, reference => reference.EntityType == "passport");
        Assert.Contains(references, reference => reference.EntityType == "customer" && reference.EntityId == "42");
    }

    [Fact]
    public void Extract_WithLowercaseIdSuffixWords_ShouldNotEmitJunkReferences()
    {
        // "paid"/"valid" end in lowercase "id" — an OrdinalIgnoreCase suffix match would mint
        // junk entity types ("pa", "val"). Only a real "Id"/"_id" suffix marks an identifier.
        var request = new PayloadCaptureRequest
        {
            Operation = "POST /api/v1/orders",
            Channel = "http",
            ContentType = "application/json",
            Payload = """{"paid":"yes","valid":"true","order_id":"a1b2","productId":7}"""
        };

        var references = PayloadEntityReferenceExtractor.Extract(request, 64, out _);

        Assert.DoesNotContain(references, reference => reference.EntityType == "pa");
        Assert.DoesNotContain(references, reference => reference.EntityType == "val");
        Assert.Contains(references, reference => reference.EntityType == "order" && reference.EntityId == "a1b2");
        Assert.Contains(references, reference => reference.EntityType == "product" && reference.EntityId == "7");
    }

    [Fact]
    public void Extract_WithCustomSensitiveNames_ShouldHonorConfiguredList()
    {
        var request = new PayloadCaptureRequest
        {
            Operation = "POST /api/v1/orders",
            Channel = "http",
            ContentType = "application/json",
            Payload = """{"loyaltyId":"L-1","customerId":42}"""
        };

        var references = PayloadEntityReferenceExtractor.Extract(request, 64, ["loyalty"], out _);

        Assert.DoesNotContain(references, reference => reference.EntityType == "loyalty");
        Assert.Contains(references, reference => reference.EntityType == "customer");
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
    public void JsonPayloadRedactor_ShouldMaskSensitiveSubstringFieldsAndEmailsInsideJsonStrings()
    {
        var redactor = new JsonPayloadRedactor(Options.Create(new PayloadCaptureOptions()));

        var redacted = redactor.Redact(
            """{"customerEmail":"ada@example.com","notes":"Contact ada@example.com today","profile":{"ownerName":"Ada"},"total":42}""",
            "application/json");

        Assert.DoesNotContain("ada@example.com", redacted);
        Assert.DoesNotContain("Ada", redacted);
        Assert.Contains("\"total\":42", redacted);
    }

    [Fact]
    public void JsonPayloadRedactor_ShouldWalkJsonArraysWithoutReparentingNodes()
    {
        var redactor = new JsonPayloadRedactor(Options.Create(new PayloadCaptureOptions()));

        var redacted = redactor.Redact(
            """{"items":[{"productId":12,"notes":"email ops@example.com"},{"productId":13}]}""",
            "application/json");

        Assert.DoesNotContain("ops@example.com", redacted);
        Assert.Contains("\"productId\":12", redacted);
        Assert.Contains("\"productId\":13", redacted);
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
    public async Task CaptureAsync_WithNullStore_ShouldReturnNullWithoutClaimingCapture()
    {
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(new NullPayloadArchiveStore(), timestamp);

        var result = await sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "inbound",
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"name":"Ada"}"""
        }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CaptureAsync_WithNullStore_AndServiceBusFailClosed_ShouldThrow()
    {
        // #78: a missing archive store must not silently fail open under FailClosed — the null-store
        // shortcut must still honor the channel's failure policy.
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(new NullPayloadArchiveStore(), timestamp, new PayloadCaptureOptions
        {
            ServiceBusFailureMode = PayloadCaptureFailureMode.FailClosed
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "outbound",
            Channel = "servicebus",
            Operation = "order.created.v1",
            ContentType = "application/json",
            Payload = """{"orderId":"1"}"""
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CaptureAsync_WhenArchiveWriteFails_FailsOpenForHttpButFailsClosedForServiceBus()
    {
        // Per-channel policy: an audit-store outage must not take down synchronous HTTP traffic
        // (FailOpen), but the background Service Bus/outbox path refuses to proceed (FailClosed).
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(new ThrowingPayloadArchiveStore(), timestamp, new PayloadCaptureOptions
        {
            HttpFailureMode = PayloadCaptureFailureMode.FailOpen,
            ServiceBusFailureMode = PayloadCaptureFailureMode.FailClosed
        });

        var httpResult = await sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "inbound",
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"name":"Ada"}"""
        }, CancellationToken.None);
        Assert.Null(httpResult);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "outbound",
            Channel = "servicebus",
            Operation = "order.created.v1",
            ContentType = "application/json",
            Payload = """{"orderId":"1"}"""
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CaptureAsync_WhenArchiveWriteFailsAndFailureModeIsFailOpen_ShouldReturnNull()
    {
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(new ThrowingPayloadArchiveStore(), timestamp, new PayloadCaptureOptions
        {
            HttpFailureMode = PayloadCaptureFailureMode.FailOpen
        });

        var result = await sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "inbound",
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"name":"Ada"}"""
        }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CaptureAsync_WhenArchiveWriteFailsAndFailureModeIsFailClosed_ShouldThrow()
    {
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(new ThrowingPayloadArchiveStore(), timestamp, new PayloadCaptureOptions
        {
            HttpFailureMode = PayloadCaptureFailureMode.FailClosed
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "inbound",
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"name":"Ada"}"""
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CaptureAsync_WhenRedactionFails_ShouldStillArchiveAndSuppressOperationalPayload()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var options = Options.Create(new PayloadCaptureOptions { HttpFailureMode = PayloadCaptureFailureMode.FailClosed });
        var sink = new PayloadCaptureSink(
            store,
            new ThrowingPayloadRedactor(),
            new FixedTimeProvider(timestamp),
            options,
            new LoggerFactory().CreateLogger<PayloadCaptureSink>());

        var result = await sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "inbound",
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = """{"email":"ada@example.com"}"""
        }, CancellationToken.None);

        Assert.NotNull(result);
        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-123.jsonl");
        Assert.Contains("ada@example.com", archiveEntry.Value.Single());
    }

    [Fact]
    public async Task CaptureAsync_ShouldMaskSensitiveQueryStringValuesInArchiveMetadataButKeepBenignParams()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = CreateSink(store, timestamp);

        await sink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = "case-123",
            Direction = "inbound",
            Channel = "http",
            Operation = "GET /api/v1/products",
            ContentType = "application/json",
            Payload = """{"ok":true}""",
            Metadata = new Dictionary<string, string>
            {
                ["method"] = "GET",
                ["path"] = "/api/v1/products",
                ["queryString"] = "?page=2&pageSize=50&token=secret-abc-123&email=ada@example.com"
            }
        }, CancellationToken.None);

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-123.jsonl");
        var auditEntry = store.Lines.Single(pair => pair.Key == "audit/2026-05-03/04/07/payload-audit.jsonl");
        var archiveLine = archiveEntry.Value.Single();

        using var archiveJson = JsonDocument.Parse(archiveLine);
        var queryString = archiveJson.RootElement.GetProperty("metadata").GetProperty("queryString").GetString();

        Assert.NotNull(queryString);
        Assert.Contains("page=2", queryString);
        Assert.Contains("pageSize=50", queryString);
        Assert.Contains("token=[REDACTED]", queryString);
        Assert.Contains("email=[REDACTED]", queryString);

        // Sensitive values must not survive anywhere in the archive or audit blob metadata.
        Assert.DoesNotContain("secret-abc-123", archiveLine);
        Assert.DoesNotContain("ada@example.com", archiveLine);
        Assert.DoesNotContain("secret-abc-123", auditEntry.Value.Single());
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

    internal static PayloadCaptureSink CreateSink(IPayloadArchiveStore store, DateTimeOffset timestamp, PayloadCaptureOptions? captureOptions = null)
    {
        var options = Options.Create(captureOptions ?? new PayloadCaptureOptions());
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

    private sealed class ThrowingPayloadArchiveStore : IPayloadArchiveStore
    {
        public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("archive store unavailable");
        }

        public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("archive store unavailable");
        }
    }

    private sealed class ThrowingPayloadRedactor : IPayloadRedactor
    {
        public string Redact(string payload, string? contentType)
        {
            throw new InvalidOperationException("redactor unavailable");
        }
    }
}
