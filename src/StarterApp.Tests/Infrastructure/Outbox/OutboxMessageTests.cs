using StarterApp.Api.Infrastructure.Outbox;
using System.Text.Json;

namespace StarterApp.Tests.Infrastructure.Outbox;

public class OutboxMessageTests
{
    [Fact]
    public void Create_WithArbitraryDomainEvent_ShouldUseStableContractAndSerializePayload()
    {
        var occurredOnUtc = new DateTimeOffset(2026, 03, 21, 04, 05, 06, TimeSpan.Zero);
        var domainEvent = new InventoryReservedDomainEvent(42, 3, null, occurredOnUtc);

        var outboxMessage = OutboxMessage.Create(domainEvent);

        Assert.Equal("inventory.reserved.v1", outboxMessage.Type);
        Assert.Equal(occurredOnUtc, outboxMessage.OccurredOnUtc);

        using var payload = JsonDocument.Parse(outboxMessage.Payload);
        Assert.Equal(42, payload.RootElement.GetProperty(nameof(InventoryReservedDomainEvent.ProductId)).GetInt32());
        Assert.Equal(3, payload.RootElement.GetProperty(nameof(InventoryReservedDomainEvent.Quantity)).GetInt32());
        Assert.Equal(occurredOnUtc, payload.RootElement.GetProperty(nameof(InventoryReservedDomainEvent.OccurredOnUtc)).GetDateTimeOffset());
        Assert.False(payload.RootElement.TryGetProperty(nameof(InventoryReservedDomainEvent.Note), out _));
    }

    [Fact]
    public void MarkAsProcessed_ShouldSetProcessedOnUtc()
    {
        var domainEvent = new InventoryReservedDomainEvent(1, 1, null, DateTimeOffset.UtcNow);
        var message = OutboxMessage.Create(domainEvent);
        var processedAt = DateTimeOffset.UtcNow;

        message.MarkAsProcessed(processedAt);

        Assert.Equal(processedAt, message.ProcessedOnUtc);
    }

    [Fact]
    public void MarkAsError_ShouldSetErrorMessage()
    {
        var domainEvent = new InventoryReservedDomainEvent(1, 1, null, DateTimeOffset.UtcNow);
        var message = OutboxMessage.Create(domainEvent);

        message.MarkAsError("Connection refused");

        Assert.Equal("Connection refused", message.Error);
    }

    [Fact]
    public void MarkAsError_WithNullOrWhitespace_ShouldThrow()
    {
        var domainEvent = new InventoryReservedDomainEvent(1, 1, null, DateTimeOffset.UtcNow);
        var message = OutboxMessage.Create(domainEvent);

        Assert.Throws<ArgumentNullException>(() => message.MarkAsError(null!));
        Assert.Throws<ArgumentException>(() => message.MarkAsError(""));
        Assert.Throws<ArgumentException>(() => message.MarkAsError("   "));
    }

    [Fact]
    public void IncrementRetry_ShouldIncrementRetryCount()
    {
        var domainEvent = new InventoryReservedDomainEvent(1, 1, null, DateTimeOffset.UtcNow);
        var message = OutboxMessage.Create(domainEvent);

        Assert.Equal(0, message.RetryCount);

        message.IncrementRetry();
        Assert.Equal(1, message.RetryCount);

        message.IncrementRetry();
        Assert.Equal(2, message.RetryCount);
    }

    private sealed record InventoryReservedDomainEvent(
        int ProductId,
        int Quantity,
        string? Note,
        DateTimeOffset OccurredOnUtc) : IDomainEvent
    {
        public string EventType => "inventory.reserved.v1";
    }
}
