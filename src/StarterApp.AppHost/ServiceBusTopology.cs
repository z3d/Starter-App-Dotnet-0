namespace StarterApp.AppHost;

public static class ServiceBusTopology
{
    public const string DomainEventsTopic = "domain-events";
    public const bool DomainEventsRequiresDuplicateDetection = true;

    // TTL + dead-lettering form the no-event-silently-lost contract on the consume side.
    // A short TTL with expiration dead-lettering DISABLED (the Azure default) deletes every
    // message that outlives a subscriber outage with no trace, while the outbox row is already
    // marked processed — silently defeating the publish side's FailClosed posture. 24h absorbs
    // an overnight consumer outage, and DeadLetteringOnMessageExpiration routes anything older
    // into the subscription DLQ for replay instead of deletion.
    public static readonly TimeSpan DomainEventsDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan DomainEventsDuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SubscriptionDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan SubscriptionLockDuration = TimeSpan.FromSeconds(30);
    public const int SubscriptionMaxDeliveryCount = 5;
    public const bool SubscriptionDeadLetteringOnMessageExpiration = true;
    public const string EmailNotificationsSubscription = "email-notifications";
    public const string InventoryReservationSubscription = "inventory-reservation";
    public const string OrderCreatedRuleName = "OrderCreatedFilter";
    public const string OrderStatusChangedRuleName = "OrderStatusChangedFilter";

    public static readonly IReadOnlyCollection<SubscriptionFilter> SubscriptionFilters =
    [
        new(EmailNotificationsSubscription, OrderCreatedRuleName, OrderCreatedEventType),
        new(EmailNotificationsSubscription, OrderStatusChangedRuleName, OrderStatusChangedEventType),
        new(InventoryReservationSubscription, OrderCreatedRuleName, OrderCreatedEventType)
    ];

    public const string OrderCreatedEventType = "order.created.v1";
    public const string OrderStatusChangedEventType = "order.status-changed.v1";
}

public sealed record SubscriptionFilter(string SubscriptionName, string RuleName, string EventType);
