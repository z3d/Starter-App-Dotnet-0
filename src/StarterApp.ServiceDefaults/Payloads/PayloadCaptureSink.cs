using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StarterApp.ServiceDefaults.Payloads;

public sealed class PayloadCaptureSink : IPayloadCaptureSink
{
    private const string RedactedValue = "[REDACTED]";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IPayloadArchiveStore _store;
    private readonly IPayloadRedactor _redactor;
    private readonly TimeProvider _timeProvider;
    private readonly PayloadCaptureOptions _options;
    private readonly ILogger<PayloadCaptureSink> _logger;

    public PayloadCaptureSink(
        IPayloadArchiveStore store,
        IPayloadRedactor redactor,
        TimeProvider timeProvider,
        IOptions<PayloadCaptureOptions> options,
        ILogger<PayloadCaptureSink> logger)
    {
        _store = store;
        _redactor = redactor;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PayloadCaptureRecord?> CaptureAsync(PayloadCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Enabled)
            return null;

        if (_store is NullPayloadArchiveStore)
        {
            _logger.LogWarning(
                "Skipped {Direction} {Channel} payload capture for {Operation} with correlation {CorrelationId}: no payload archive store is configured",
                request.Direction,
                request.Channel,
                request.Operation,
                CorrelationContext.Sanitize(request.CorrelationId ?? CorrelationContext.GetOrCreate()));

            // A missing store must not silently fail open even though RequireArchiveStore also guards it at startup.
            if (_options.FailureModeFor(request.Channel) == PayloadCaptureFailureMode.FailClosed)
                throw new InvalidOperationException(
                    $"Payload capture is FailClosed for channel '{request.Channel}', but no payload archive store is configured; refusing to proceed without an audit record.");

            return null;
        }

        var timestampUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var correlationId = CorrelationContext.Sanitize(request.CorrelationId ?? CorrelationContext.GetOrCreate());
        var archiveBlobName = PayloadBlobNaming.BuildArchiveBlobName(timestampUtc, correlationId, _options.ArchivePrefix);
        var auditBlobName = PayloadBlobNaming.BuildAuditBlobName(timestampUtc, _options.AuditPrefix);
        var entityReferences = PayloadEntityReferenceExtractor.Extract(request, _options.MaxEntityReferences, _options.SensitivePropertyNames, out var entityReferencesTruncated);
        var capturedPayloadBytes = request.CapturedPayloadBytes == 0 && request.Payload.Length > 0
            ? Encoding.UTF8.GetByteCount(request.Payload)
            : request.CapturedPayloadBytes;
        var payloadSizeBytes = request.PayloadSizeBytes ?? capturedPayloadBytes;

        var archiveMetadata = BuildArchiveMetadata(request.Metadata);
        if (entityReferencesTruncated)
            archiveMetadata["entityReferencesTruncated"] = "true";

        var record = new PayloadCaptureRecord
        {
            OperationId = Guid.CreateVersion7().ToString("N"),
            TimestampUtc = timestampUtc,
            CorrelationId = correlationId,
            Direction = request.Direction,
            Channel = request.Channel,
            Operation = request.Operation,
            ContentType = request.ContentType,
            StatusCode = request.StatusCode,
            ArchiveBlobName = archiveBlobName,
            AuditBlobName = auditBlobName,
            PayloadSha256 = ComputeSha256(request.Payload),
            Payload = request.Payload,
            PayloadTruncated = request.PayloadTruncated,
            PayloadSizeBytes = payloadSizeBytes,
            CapturedPayloadBytes = capturedPayloadBytes,
            PayloadSkipReason = request.PayloadSkipReason,
            Metadata = archiveMetadata,
            EntityReferences = entityReferences.ToList()
        };

        try
        {
            // The durable record; the only append governed by the channel's failure mode.
            var line = JsonSerializer.Serialize(record, SerializerOptions);
            await _store.AppendLineAsync(archiveBlobName, line, cancellationToken);

            // Best-effort: the most contended write in the system must never fail traffic or pause the outbox, even FailClosed.
            await AppendAuditBestEffortAsync(auditBlobName, line, request, cancellationToken);

            var entityIndexBlobNames = new List<string>();
            foreach (var entityReference in entityReferences)
            {
                var entityIndexBlobName = PayloadBlobNaming.BuildEntityIndexBlobName(timestampUtc, entityReference, correlationId, _options.EntityIndexPrefix);
                var entityIndexRecord = new PayloadEntityIndexRecord
                {
                    OperationId = record.OperationId,
                    TimestampUtc = timestampUtc,
                    CorrelationId = correlationId,
                    Direction = request.Direction,
                    Channel = request.Channel,
                    Operation = request.Operation,
                    EntityType = entityReference.EntityType,
                    EntityId = entityReference.EntityId,
                    ArchiveBlobName = archiveBlobName,
                    AuditBlobName = auditBlobName,
                    PayloadSha256 = record.PayloadSha256,
                    Metadata = BuildEntityIndexMetadata(request.Metadata)
                };

                await _store.AppendLineAsync(entityIndexBlobName, JsonSerializer.Serialize(entityIndexRecord, SerializerOptions), cancellationToken);
                entityIndexBlobNames.Add(entityIndexBlobName);
            }

            var redactedPayload = RedactForLog(request);
            _logger.LogInformation(
                "Captured {Direction} {Channel} payload for {Operation} with correlation {CorrelationId}. ArchiveBlob: {ArchiveBlobName}. AuditBlob: {AuditBlobName}. EntityIndexBlobs: {EntityIndexBlobNames}. Truncated: {PayloadTruncated}. SkipReason: {PayloadSkipReason}. Payload: {RedactedPayload}",
                request.Direction,
                request.Channel,
                request.Operation,
                correlationId,
                archiveBlobName,
                auditBlobName,
                entityIndexBlobNames.Count == 0 ? "(none)" : string.Join(",", entityIndexBlobNames),
                request.PayloadTruncated,
                request.PayloadSkipReason ?? "(none)",
                redactedPayload);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && _options.FailureModeFor(request.Channel) == PayloadCaptureFailureMode.FailOpen)
        {
            _logger.LogError(ex,
                "Payload capture failed for {Direction} {Channel} {Operation} with correlation {CorrelationId}; continuing because FailureMode is FailOpen",
                request.Direction,
                request.Channel,
                request.Operation,
                correlationId);
            return null;
        }

        return record;
    }

    private async Task AppendAuditBestEffortAsync(string auditBlobName, string line, PayloadCaptureRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _store.AppendLineAsync(auditBlobName, line, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Audit-stream append to {AuditBlobName} failed for {Direction} {Channel} {Operation}; the archive record is durable, continuing",
                auditBlobName,
                request.Direction,
                request.Channel,
                request.Operation);
        }
    }

    private string RedactForLog(PayloadCaptureRequest request)
    {
        try
        {
            return _redactor.Redact(request.Payload, request.ContentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Payload redaction failed for {Direction} {Channel} {Operation}; suppressing payload in operational logs",
                request.Direction,
                request.Channel,
                request.Operation);
            return "[payload redaction failed]";
        }
    }

    private Dictionary<string, string> BuildEntityIndexMetadata(Dictionary<string, string> metadata)
    {
        return metadata
            .Where(pair => !IsSensitiveMetadataKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    // The query string can carry bearer secrets, so sensitive parameter values are masked; the entity index drops it entirely.
    private Dictionary<string, string> BuildArchiveMetadata(Dictionary<string, string> metadata)
    {
        return metadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Key.Equals("queryString", StringComparison.OrdinalIgnoreCase)
                ? RedactSensitiveQueryStringValues(pair.Value)
                : pair.Value,
            StringComparer.Ordinal);
    }

    private string RedactSensitiveQueryStringValues(string queryString)
    {
        if (string.IsNullOrEmpty(queryString))
            return queryString;

        var hasLeadingQuestionMark = queryString.StartsWith('?');
        var body = hasLeadingQuestionMark ? queryString[1..] : queryString;
        if (body.Length == 0)
            return queryString;

        var pairs = body.Split('&');
        for (var index = 0; index < pairs.Length; index++)
        {
            var pair = pairs[index];
            var separatorIndex = pair.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
                continue;

            var name = pair[..separatorIndex];
            if (IsSensitiveParameterName(name))
                pairs[index] = string.Concat(name, "=", RedactedValue);
        }

        var rebuilt = string.Join('&', pairs);
        return hasLeadingQuestionMark ? string.Concat("?", rebuilt) : rebuilt;
    }

    private bool IsSensitiveParameterName(string name)
    {
        var decodedName = Uri.UnescapeDataString(name);
        return _options.SensitivePropertyNames.Any(sensitiveName => decodedName.Contains(sensitiveName, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSensitiveMetadataKey(string key)
    {
        if (key.Equals("queryString", StringComparison.OrdinalIgnoreCase))
            return true;

        return _options.SensitivePropertyNames.Any(sensitiveName => key.Contains(sensitiveName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
