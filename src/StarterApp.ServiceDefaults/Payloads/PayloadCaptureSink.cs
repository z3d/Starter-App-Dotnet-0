using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StarterApp.ServiceDefaults.Payloads;

public sealed class PayloadCaptureSink : IPayloadCaptureSink
{
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

        var timestampUtc = (request.TimestampUtc ?? _timeProvider.GetUtcNow()).ToUniversalTime();
        var correlationId = CorrelationContext.Sanitize(request.CorrelationId ?? CorrelationContext.GetOrCreate());
        var archiveBlobName = PayloadBlobNaming.BuildArchiveBlobName(timestampUtc, correlationId, _options.ArchivePrefix);
        var auditBlobName = PayloadBlobNaming.BuildAuditBlobName(timestampUtc, _options.AuditPrefix);

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
            Metadata = request.Metadata
        };

        var line = JsonSerializer.Serialize(record, SerializerOptions);
        await _store.AppendLineAsync(archiveBlobName, line, cancellationToken);
        await _store.AppendLineAsync(auditBlobName, line, cancellationToken);

        var redactedPayload = _redactor.Redact(request.Payload, request.ContentType);
        _logger.LogInformation(
            "Captured {Direction} {Channel} payload for {Operation} with correlation {CorrelationId}. ArchiveBlob: {ArchiveBlobName}. AuditBlob: {AuditBlobName}. Payload: {Payload}",
            request.Direction,
            request.Channel,
            request.Operation,
            correlationId,
            archiveBlobName,
            auditBlobName,
            redactedPayload);

        return record;
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
