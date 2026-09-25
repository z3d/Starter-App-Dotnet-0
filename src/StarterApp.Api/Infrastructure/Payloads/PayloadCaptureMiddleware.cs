using System.Text;
using Microsoft.Extensions.Options;
using Serilog.Context;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.Payloads;

public sealed class PayloadCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly PayloadCaptureOptions _options;
    private readonly ILogger<PayloadCaptureMiddleware> _logger;

    public PayloadCaptureMiddleware(
        RequestDelegate next,
        IPayloadCaptureSink payloadCaptureSink,
        IOptions<PayloadCaptureOptions> options,
        ILogger<PayloadCaptureMiddleware> logger)
    {
        _next = next;
        _payloadCaptureSink = payloadCaptureSink;
        _options = options.Value;
        _logger = logger;
    }

    // Hardcoded on purpose: only probe noise is skipped, and configuration would let business routes opt out.
    internal static readonly IReadOnlySet<string> ProbeSkipRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        "/health",
        "/health/ready",
        "/health/live",
        "/alive",
        "/liveness",
        "/healthiness"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Value is { } path && ProbeSkipRoutes.Contains(path))
        {
            await _next(context);
            return;
        }

        var correlationId = ResolveCorrelationId(context, out var hadInboundCorrelationId);
        context.TraceIdentifier = correlationId;

        // The caller's request headers stay untouched; the sanitized id is used wherever it is reflected or persisted.
        if (!hadInboundCorrelationId)
            context.Request.Headers[CorrelationContext.HeaderName] = correlationId;

        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;

        // UseExceptionHandler clears response headers; an OnStarting callback survives that, so the echo reaches error responses.
        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse, string))state;
            response.Headers[CorrelationContext.HeaderName] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));

        using var correlationScope = CorrelationContext.Push(correlationId);
        using var logScope = LogContext.PushProperty("CorrelationId", correlationId);

        await CaptureRequestAsync(context, correlationId);

        var originalResponseBody = context.Response.Body;
        await using var responseBody = new BoundedCaptureStream(originalResponseBody, _options.MaxPayloadBytes);
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
            await CaptureResponseAsync(context, correlationId, responseBody);
        }
        catch (OperationCanceledException)
        {
            // A client abort must not suppress the audit record, so capture the buffered bytes with an unlinked token.
            _logger.LogWarning("HTTP request was canceled for correlation {CorrelationId}; capturing the partial response for audit", correlationId);
            await CaptureResponseAsync(context, correlationId, responseBody);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP request failed before response payload capture completed for correlation {CorrelationId}", correlationId);
            throw;
        }
        finally
        {
            context.Response.Body = originalResponseBody;
        }
    }

    private async Task CaptureRequestAsync(HttpContext context, string correlationId)
    {
        var metadata = new Dictionary<string, string>
        {
            ["method"] = context.Request.Method,
            ["path"] = context.Request.Path.Value ?? string.Empty,
            ["queryString"] = context.Request.QueryString.Value ?? string.Empty
        };

        if (!ShouldCaptureContentType(context.Request.ContentType))
        {
            await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
            {
                CorrelationId = correlationId,
                Direction = "inbound",
                Channel = PayloadCaptureChannels.Http,
                Operation = $"{context.Request.Method} {context.Request.Path}",
                ContentType = context.Request.ContentType,
                PayloadSkipReason = BuildUnsupportedContentTypeReason(context.Request.ContentType),
                PayloadSizeBytes = context.Request.ContentLength,
                Metadata = metadata
            }, context.RequestAborted);
            return;
        }

        context.Request.EnableBuffering();

        var payload = await ReadStreamAsync(context.Request.Body, _options.MaxPayloadBytes, context.Request.ContentLength, context.RequestAborted);
        context.Request.Body.Position = 0;

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = correlationId,
            Direction = "inbound",
            Channel = PayloadCaptureChannels.Http,
            Operation = $"{context.Request.Method} {context.Request.Path}",
            ContentType = context.Request.ContentType,
            Payload = payload.Value,
            PayloadTruncated = payload.Truncated,
            PayloadSizeBytes = payload.PayloadSizeBytes,
            CapturedPayloadBytes = payload.CapturedPayloadBytes,
            PayloadSkipReason = payload.Truncated ? $"Payload exceeded configured limit of {_options.MaxPayloadBytes} bytes" : null,
            Metadata = metadata
        }, context.RequestAborted);
    }

    private async Task CaptureResponseAsync(HttpContext context, string correlationId, BoundedCaptureStream responseBody)
    {
        var metadata = new Dictionary<string, string>
        {
            ["method"] = context.Request.Method,
            ["path"] = context.Request.Path.Value ?? string.Empty,
            ["statusCode"] = context.Response.StatusCode.ToString(CultureInfo.InvariantCulture),
            ["action"] = AuditAction.Resolve(context)
        };

        // Audit blobs may carry PII by policy; logs stay redacted.
        if (context.RequestServices?.GetService<ICurrentUser>() is { IsAuthenticated: true } currentUser)
        {
            metadata["subject"] = currentUser.Subject;
            metadata["tenantId"] = currentUser.TenantId;
        }

        // CancellationToken.None: a client abort must not suppress the audit record after the response was produced.
        if (!ShouldCaptureContentType(context.Response.ContentType))
        {
            await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
            {
                CorrelationId = correlationId,
                Direction = "outbound",
                Channel = PayloadCaptureChannels.Http,
                Operation = $"{context.Request.Method} {context.Request.Path}",
                ContentType = context.Response.ContentType,
                StatusCode = context.Response.StatusCode,
                PayloadSkipReason = BuildUnsupportedContentTypeReason(context.Response.ContentType),
                PayloadSizeBytes = responseBody.TotalBytesWritten,
                Metadata = metadata
            }, CancellationToken.None);
            return;
        }

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = correlationId,
            Direction = "outbound",
            Channel = PayloadCaptureChannels.Http,
            Operation = $"{context.Request.Method} {context.Request.Path}",
            ContentType = context.Response.ContentType,
            StatusCode = context.Response.StatusCode,
            Payload = responseBody.GetCapturedPayload(),
            PayloadTruncated = responseBody.Truncated,
            PayloadSizeBytes = responseBody.TotalBytesWritten,
            CapturedPayloadBytes = responseBody.CapturedBytes,
            PayloadSkipReason = responseBody.Truncated ? $"Payload exceeded configured limit of {_options.MaxPayloadBytes} bytes" : null,
            Metadata = metadata
        }, CancellationToken.None);
    }

    private static string ResolveCorrelationId(HttpContext context, out bool hadInboundCorrelationId)
    {
        if (context.Request.Headers.TryGetValue(CorrelationContext.HeaderName, out var values) && !string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            hadInboundCorrelationId = true;
            return CorrelationContext.Sanitize(values.First()!);
        }

        hadInboundCorrelationId = false;
        return CorrelationContext.Create();
    }

    private bool ShouldCaptureContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return true;

        if (_options.CapturedContentTypes.Length == 0)
            return true;

        var mediaType = contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        return _options.CapturedContentTypes.Any(pattern => MatchesContentTypePattern(mediaType, pattern));
    }

    private static bool MatchesContentTypePattern(string mediaType, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*/*")
            return true;

        var normalizedPattern = pattern.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        if (mediaType.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedPattern.EndsWith("/*", StringComparison.Ordinal))
            return mediaType.StartsWith(normalizedPattern[..^1], StringComparison.OrdinalIgnoreCase);

        if (normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            var parts = normalizedPattern.Split('*', 2);
            return mediaType.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) &&
                mediaType.EndsWith(parts[1], StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string BuildUnsupportedContentTypeReason(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            ? "Content type is not configured for payload capture"
            : $"Content type '{contentType}' is not configured for payload capture";
    }

    private static async Task<LimitedPayload> ReadStreamAsync(Stream stream, int maxBytes, long? knownLengthBytes, CancellationToken cancellationToken)
    {
        if (!stream.CanRead)
            return new LimitedPayload(string.Empty, false, knownLengthBytes, 0);

        var readLimit = checked(maxBytes + 1);
        var buffer = new byte[Math.Min(8192, readLimit)];
        long bytesRead = 0;
        await using var captured = new MemoryStream(Math.Min(maxBytes, 8192));

        while (bytesRead < readLimit)
        {
            var bytesRemainingBeforeTruncationMarker = (int)Math.Min(buffer.Length, readLimit - bytesRead);
            var read = await stream.ReadAsync(buffer.AsMemory(0, bytesRemainingBeforeTruncationMarker), cancellationToken);
            if (read == 0)
                break;

            bytesRead += read;
            var captureRemaining = maxBytes - captured.Length;
            if (captureRemaining > 0)
            {
                var captureCount = (int)Math.Min(read, captureRemaining);
                await captured.WriteAsync(buffer.AsMemory(0, captureCount), cancellationToken);
            }
        }

        var truncated = bytesRead > maxBytes || knownLengthBytes > maxBytes;
        var payloadSizeBytes = knownLengthBytes ?? (truncated ? null : bytesRead);
        return new LimitedPayload(Encoding.UTF8.GetString(captured.ToArray()), truncated, payloadSizeBytes, (int)captured.Length);
    }

    private sealed record LimitedPayload(
        string Value,
        bool Truncated,
        long? PayloadSizeBytes,
        int CapturedPayloadBytes);

    private sealed class BoundedCaptureStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _maxCapturedBytes;
        private readonly MemoryStream _captured;

        public BoundedCaptureStream(Stream inner, int maxCapturedBytes)
        {
            _inner = inner;
            _maxCapturedBytes = maxCapturedBytes;
            _captured = new MemoryStream(Math.Min(maxCapturedBytes, 8192));
        }

        public long TotalBytesWritten { get; private set; }
        public int CapturedBytes => (int)_captured.Length;
        public bool Truncated => TotalBytesWritten > _maxCapturedBytes;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string GetCapturedPayload()
        {
            return Encoding.UTF8.GetString(_captured.ToArray());
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Capture(buffer);
            _inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        private void Capture(ReadOnlySpan<byte> buffer)
        {
            TotalBytesWritten += buffer.Length;
            var remaining = _maxCapturedBytes - _captured.Length;
            if (remaining <= 0)
                return;

            _captured.Write(buffer[..Math.Min(buffer.Length, (int)remaining)]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _captured.Dispose();

            base.Dispose(disposing);
        }
    }
}
