namespace StarterApp.Tests.Infrastructure;

public class OutboxReplayMessageTests
{
    private static OutboxMessage CreateMessage()
    {
        var order = new Order(Guid.CreateVersion7(), 42, "replay-owner", "replay-tenant");
        order.AddItem(7, "Replay Product", 1, Money.Create(10m, "USD"));
        return OutboxMessage.Create(new OrderCreatedDomainEvent(order));
    }

    [Fact]
    public void ResetForReplay_OnErroredMessage_ClearsErrorStateAndStampsReplayMetadata()
    {
        var message = CreateMessage();
        message.IncrementRetry();
        message.MarkAsError("boom");
        var replayedAt = DateTimeOffset.UtcNow;

        message.ResetForReplay(replayedAt);

        Assert.Null(message.Error);
        Assert.Equal(0, message.RetryCount);
        Assert.Equal(1, message.ReplayCount);
        Assert.Equal(replayedAt, message.ReplayedOnUtc);
        Assert.Null(message.ProcessingId);
        Assert.Null(message.LockedUntilUtc);
    }

    [Fact]
    public void ResetForReplay_OnProcessedMessage_Throws()
    {
        var message = CreateMessage();
        message.MarkAsProcessed(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => message.ResetForReplay(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ResetForReplay_OnNonErroredMessage_Throws()
    {
        var message = CreateMessage();

        Assert.Throws<InvalidOperationException>(() => message.ResetForReplay(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BuildServiceBusMessage_ForFirstDelivery_CarriesNoReplayMarker()
    {
        var message = CreateMessage();

        var serviceBusMessage = OutboxProcessor.BuildServiceBusMessage(message);

        Assert.False(serviceBusMessage.ApplicationProperties.ContainsKey("Replay"));
        Assert.False(serviceBusMessage.ApplicationProperties.ContainsKey("ReplayCount"));
        Assert.Equal(message.Type, serviceBusMessage.Subject);
        Assert.Equal(message.CorrelationId, serviceBusMessage.CorrelationId);
    }

    [Fact]
    public void BuildServiceBusMessage_ForReplayedMessage_CarriesReplayMarker()
    {
        var message = CreateMessage();
        message.MarkAsError("boom");
        message.ResetForReplay(DateTimeOffset.UtcNow);

        var serviceBusMessage = OutboxProcessor.BuildServiceBusMessage(message);

        Assert.Equal(true, serviceBusMessage.ApplicationProperties["Replay"]);
        Assert.Equal(1, serviceBusMessage.ApplicationProperties["ReplayCount"]);
    }
}
