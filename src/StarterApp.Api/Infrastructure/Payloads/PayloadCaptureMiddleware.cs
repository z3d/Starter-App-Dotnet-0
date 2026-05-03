using Serilog.Context;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.Payloads;

public sealed class PayloadCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly ILogger<PayloadCaptureMiddleware> _logger;

    public PayloadCaptureMiddleware(
        RequestDelegate next,
        IPayloadCaptureSink payloadCaptureSink,
        ILogger<PayloadCaptureMiddleware> logger)
    {
        _next = next;
        _payloadCaptureSink = payloadCaptureSink;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;

        using var correlationScope = CorrelationContext.Push(correlationId);
        using var logScope = LogContext.PushProperty("CorrelationId", correlationId);

        await CaptureRequestAsync(context, correlationId);

        var originalResponseBody = context.Response.Body;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
            await CaptureResponseAsync(context, correlationId, responseBody);
            responseBody.Position = 0;
            await responseBody.CopyToAsync(originalResponseBody, context.RequestAborted);
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
        context.Request.EnableBuffering();

        var payload = await ReadStreamAsync(context.Request.Body, context.RequestAborted);
        context.Request.Body.Position = 0;

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = correlationId,
            Direction = "inbound",
            Channel = "http",
            Operation = $"{context.Request.Method} {context.Request.Path}",
            ContentType = context.Request.ContentType,
            Payload = payload,
            Metadata = new Dictionary<string, string>
            {
                ["method"] = context.Request.Method,
                ["path"] = context.Request.Path.Value ?? string.Empty,
                ["queryString"] = context.Request.QueryString.Value ?? string.Empty
            }
        }, context.RequestAborted);
    }

    private async Task CaptureResponseAsync(HttpContext context, string correlationId, MemoryStream responseBody)
    {
        responseBody.Position = 0;
        var payload = await ReadStreamAsync(responseBody, context.RequestAborted);
        responseBody.Position = 0;

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = correlationId,
            Direction = "outbound",
            Channel = "http",
            Operation = $"{context.Request.Method} {context.Request.Path}",
            ContentType = context.Response.ContentType,
            StatusCode = context.Response.StatusCode,
            Payload = payload,
            Metadata = new Dictionary<string, string>
            {
                ["method"] = context.Request.Method,
                ["path"] = context.Request.Path.Value ?? string.Empty,
                ["statusCode"] = context.Response.StatusCode.ToString()
            }
        }, context.RequestAborted);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationContext.HeaderName, out var values) && !string.IsNullOrWhiteSpace(values.FirstOrDefault()))
            return CorrelationContext.Sanitize(values.First()!);

        return CorrelationContext.Create();
    }

    private static async Task<string> ReadStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanRead)
            return string.Empty;

        using var reader = new StreamReader(stream, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
