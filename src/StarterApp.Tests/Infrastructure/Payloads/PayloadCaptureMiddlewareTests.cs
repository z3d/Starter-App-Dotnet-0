using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StarterApp.Api.Infrastructure.Payloads;
using StarterApp.ServiceDefaults.Payloads;
using System.Text;
using System.Text.Json;

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
