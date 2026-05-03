using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.Functions;
using StarterApp.ServiceDefaults.Payloads;
using StarterApp.Tests.Infrastructure.Payloads;

namespace StarterApp.Tests.Infrastructure.Functions;

public class PayloadFunctionTests
{
    [Fact]
    public async Task ServiceBusFunction_ShouldCaptureInboundPayloadWithCorrelation()
    {
        var store = new InMemoryPayloadArchiveStore();
        var timestamp = new DateTimeOffset(2026, 5, 3, 4, 7, 0, TimeSpan.Zero);
        var sink = PayloadCaptureTests.CreateSink(store, timestamp);
        var function = new OrderConfirmationEmailFunction(new LoggerFactory().CreateLogger<OrderConfirmationEmailFunction>(), sink);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{"email":"ada@example.com","orderId":"abc"}"""),
            messageId: "message-1",
            subject: "order.created.v1",
            contentType: "application/json",
            correlationId: "case-789");

        await function.RunAsync(message, CancellationToken.None);

        var archiveEntry = store.Lines.Single(pair => pair.Key == "archive/2026-05-03/04/07/case-789.jsonl");
        var entityIndexEntry = store.Lines.Single(pair => pair.Key == "entity-index/order/abc/2026-05-03/04/07/case-789.jsonl");
        Assert.Contains("\"channel\":\"servicebus\"", archiveEntry.Value.Single());
        Assert.Contains("ada@example.com", archiveEntry.Value.Single());
        Assert.Contains("\"archiveBlobName\":\"archive/2026-05-03/04/07/case-789.jsonl\"", entityIndexEntry.Value.Single());
        Assert.DoesNotContain("ada@example.com", entityIndexEntry.Value.Single());
    }

    [Fact]
    public async Task CleanupFunction_ShouldDeletePayloadBlobsOlderThanRetention()
    {
        var store = new InMemoryPayloadArchiveStore();
        await store.AppendLineAsync("archive/2026-05-01/00/00/case-old.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("audit/2026-05-01/00/00/payload-audit.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("entity-index/customer/42/2026-05-01/00/00/case-old.jsonl", "{}", CancellationToken.None);
        await store.AppendLineAsync("archive/2026-05-09/00/00/case-new.jsonl", "{}", CancellationToken.None);

        var options = Options.Create(new PayloadCaptureOptions { RetentionDays = 7 });
        var function = new PayloadArchiveCleanupFunction(
            store,
            new PayloadCaptureTests.FixedTimeProvider(new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero)),
            options,
            new LoggerFactory().CreateLogger<PayloadArchiveCleanupFunction>());

        await function.RunAsync(null!, CancellationToken.None);

        Assert.DoesNotContain("archive/2026-05-01/00/00/case-old.jsonl", store.Lines.Keys);
        Assert.DoesNotContain("audit/2026-05-01/00/00/payload-audit.jsonl", store.Lines.Keys);
        Assert.DoesNotContain("entity-index/customer/42/2026-05-01/00/00/case-old.jsonl", store.Lines.Keys);
        Assert.Contains("archive/2026-05-09/00/00/case-new.jsonl", store.Lines.Keys);
    }
}
