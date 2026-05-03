using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StarterApp.Api.Infrastructure.Payloads;
using StarterApp.ServiceDefaults.Payloads;
using System.Text;

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
        }, sink, new LoggerFactory().CreateLogger<PayloadCaptureMiddleware>());

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

        Assert.Equal(2, archiveEntry.Value.Count);
        Assert.Equal(2, auditEntry.Value.Count);
        Assert.Contains("ada@example.com", archiveEntry.Value[0]);
        Assert.Contains("response@example.com", archiveEntry.Value[1]);
        Assert.Equal("case-456", context.Response.Headers[CorrelationContext.HeaderName]);
    }
}
