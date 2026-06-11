using System.Text;
using Microsoft.Extensions.Logging;
using StarterApp.Api.Infrastructure.Payloads;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class PayloadCaptureMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldCaptureInboundAndOutboundHttpPayloadsForCorrelation()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = PayloadCaptureTests.CreateSink(store, timestamp);
        var middleware = new PayloadCaptureMiddleware(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"id":42,"email":"response@example.com"}""");
        }, sink, Microsoft.Extensions.Options.Options.Create(new PayloadCaptureOptions()), new LoggerFactory().CreateLogger<PayloadCaptureMiddleware>());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/customers";
        context.Request.ContentType = "application/json";
        context.Request.Headers[CorrelationContext.HeaderName] = "case-456";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"name":"Ada","email":"ada@example.com"}"""));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-456.jsonl");
        var auditEntry = store.Lines.Single(pair => pair.Key == "audit/2026-05-03/04/07/payload-audit.jsonl");
        var entityIndexEntry = store.Lines.Single(pair => pair.Key == "entity-index/customer/42/2026-05-03/04/07/case-456.jsonl");

        Assert.Equal(2, archiveEntry.Value.Count);
        Assert.Equal(2, auditEntry.Value.Count);
        Assert.Contains("ada@example.com", archiveEntry.Value[0]);
        Assert.Contains("response@example.com", archiveEntry.Value[1]);
        Assert.Contains("\"archiveBlobName\":\"archive/2026-05-03/04/07/case-456.jsonl\"", entityIndexEntry.Value.Single());
        Assert.DoesNotContain("response@example.com", entityIndexEntry.Value.Single());
        Assert.Equal("case-456", context.Response.Headers[CorrelationContext.HeaderName]);
    }

    [Fact]
    public async Task InvokeAsync_WithPresentCorrelationId_ShouldLeaveRequestHeaderRawForGatewayButEchoSanitized()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = PayloadCaptureTests.CreateSink(store, timestamp);
        string? downstreamCorrelationId = null;
        var middleware = new PayloadCaptureMiddleware(context =>
        {
            downstreamCorrelationId = context.Request.Headers[CorrelationContext.HeaderName].ToString();
            return Task.CompletedTask;
        }, sink, Microsoft.Extensions.Options.Options.Create(new PayloadCaptureOptions()), new LoggerFactory().CreateLogger<PayloadCaptureMiddleware>());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/v1/customers";
        context.Request.Headers[CorrelationContext.HeaderName] = "trace:abc";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        // The gateway identity layer validates the raw value the gateway signed, so a present correlation id
        // must reach the downstream pipeline unchanged (not pre-sanitized by this middleware)...
        Assert.Equal("trace:abc", downstreamCorrelationId);

        // ...while the echoed correlation id stays sanitized, so raw client input is never reflected
        // back. Lossy sanitization appends a short raw-bound hash so distinct raw ids stay distinct.
        Assert.Matches(@"^traceabc\.[0-9a-f]{8}$", context.Response.Headers[CorrelationContext.HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WithoutCorrelationId_ShouldInjectGeneratedIdForDownstream()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = PayloadCaptureTests.CreateSink(store, timestamp);
        string? downstreamCorrelationId = null;
        var middleware = new PayloadCaptureMiddleware(context =>
        {
            downstreamCorrelationId = context.Request.Headers[CorrelationContext.HeaderName].ToString();
            return Task.CompletedTask;
        }, sink, Microsoft.Extensions.Options.Options.Create(new PayloadCaptureOptions()), new LoggerFactory().CreateLogger<PayloadCaptureMiddleware>());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/v1/customers";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        // When the caller sends none, a generated (contract-valid) id is injected so the gateway's
        // required-header check still passes.
        Assert.False(string.IsNullOrWhiteSpace(downstreamCorrelationId));
    }

    [Fact]
    public async Task InvokeAsync_WhenClientAbortsAfterResponse_ShouldStillCaptureResponseForAudit()
    {
        // A caller could deliberately disconnect after receiving the response to keep it out of
        // the audit trail: the response capture must run on an unlinked token and the middleware
        // must capture before rethrowing the cancellation.
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = PayloadCaptureTests.CreateSink(store, timestamp);
        using var abortSource = new CancellationTokenSource();
        var middleware = new PayloadCaptureMiddleware(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"id":7,"secretOutcome":"approved"}""");
            await abortSource.CancelAsync();
            abortSource.Token.ThrowIfCancellationRequested();
        }, sink, Microsoft.Extensions.Options.Options.Create(new PayloadCaptureOptions()), new LoggerFactory().CreateLogger<PayloadCaptureMiddleware>());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/v1/orders";
        context.Request.Headers[CorrelationContext.HeaderName] = "case-abort";
        context.RequestAborted = abortSource.Token;
        context.Response.Body = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-abort.jsonl");
        Assert.Equal(2, archiveEntry.Value.Count);
        Assert.Contains("approved", archiveEntry.Value[1]);
    }

    [Fact]
    public async Task InvokeAsync_ShouldBoundCapturedPayloadsAndRecordTruncationMetadata()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var options = new PayloadCaptureOptions { MaxPayloadBytes = 12 };
        var sink = PayloadCaptureTests.CreateSink(store, timestamp, options);
        var middleware = new PayloadCaptureMiddleware(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"description":"response payload is too large"}""");
        }, sink, Microsoft.Extensions.Options.Options.Create(options), new LoggerFactory().CreateLogger<PayloadCaptureMiddleware>());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/customers";
        context.Request.ContentType = "application/json";
        context.Request.Headers[CorrelationContext.HeaderName] = "case-large";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"description":"request payload is too large"}"""));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-large.jsonl");
        Assert.Equal(2, archiveEntry.Value.Count);

        using var requestJson = JsonDocument.Parse(archiveEntry.Value[0]);
        using var responseJson = JsonDocument.Parse(archiveEntry.Value[1]);
        Assert.True(requestJson.RootElement.GetProperty("payloadTruncated").GetBoolean());
        Assert.True(responseJson.RootElement.GetProperty("payloadTruncated").GetBoolean());
        Assert.Equal(12, requestJson.RootElement.GetProperty("capturedPayloadBytes").GetInt32());
        Assert.Equal(12, responseJson.RootElement.GetProperty("capturedPayloadBytes").GetInt32());
        Assert.Contains("configured limit", requestJson.RootElement.GetProperty("payloadSkipReason").GetString());
        Assert.Contains("configured limit", responseJson.RootElement.GetProperty("payloadSkipReason").GetString());
    }
}
