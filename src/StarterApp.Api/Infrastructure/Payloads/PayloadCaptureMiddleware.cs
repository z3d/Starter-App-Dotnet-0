using Microsoft.Extensions.Options;
using Serilog.Context;
using StarterApp.ServiceDefaults.Payloads;
using System.Text;

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

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Request.Headers[CorrelationContext.HeaderName] = correlationId;
        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;

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
        catch (Exception ex) when (ex is not OperationCanceledException)
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
                Channel = "http",
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
            Channel = "http",
            Operation = $"{context.Request.Method} {context.Request.Path}",
            ContentType = context.Request.ContentType,
            Payload = payload,
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
            ["statusCode"] = context.Response.StatusCode.ToString()
        };

        if (!ShouldCaptureContentType(context.Response.ContentType))
        {
            await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
            {
                CorrelationId = correlationId,
                Direction = "outbound",
                Channel = "http",
                Operation = $"{context.Request.Method} {context.Request.Path}",
                ContentType = context.Response.ContentType,
                StatusCode = context.Response.StatusCode,
                PayloadSkipReason = BuildUnsupportedContentTypeReason(context.Response.ContentType),
                PayloadSizeBytes = responseBody.TotalBytesWritten,
                Metadata = metadata
            }, context.RequestAborted);
            return;
        }

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = correlationId,
            Direction = "outbound",
            Channel = "http",
            Operation = $"{context.Request.Method} {context.Request.Path}",
            ContentType = context.Response.ContentType,
            StatusCode = context.Response.StatusCode,
            Payload = responseBody.GetCapturedPayload(),
            PayloadTruncated = responseBody.Truncated,
            PayloadSizeBytes = responseBody.TotalBytesWritten,
            CapturedPayloadBytes = responseBody.CapturedBytes,
            PayloadSkipReason = responseBody.Truncated ? $"Payload exceeded configured limit of {_options.MaxPayloadBytes} bytes" : null,
            Metadata = metadata
        }, context.RequestAborted);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationContext.HeaderName, out var values) && !string.IsNullOrWhiteSpace(values.FirstOrDefault()))
            return CorrelationContext.Sanitize(values.First()!);

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
        int CapturedPayloadBytes)
    {
        public static implicit operator string(LimitedPayload payload)
        {
            return payload.Value;
        }
    }

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
