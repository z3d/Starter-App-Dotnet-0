namespace StarterApp.AppHost;

public static class ServiceBusTopology
{
    public const string DomainEventsTopic = "domain-events";
    public const bool DomainEventsRequiresDuplicateDetection = true;

    // The 24-hour TTL covers an overnight subscriber outage. Expired events go to the
    // dead-letter queue for replay; with the Azure default they would be deleted with no trace,
    // after the outbox had already marked them as published.
    public static readonly TimeSpan DomainEventsDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan DomainEventsDuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SubscriptionDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan SubscriptionLockDuration = TimeSpan.FromSeconds(30);
    public const int SubscriptionMaxDeliveryCount = 5;
    public const bool SubscriptionDeadLetteringOnMessageExpiration = true;

    // The Service Bus emulator refuses any TTL above one hour at startup and crash-loops.
    // The clamp applies only when running against the emulator; the published Azure resources
    // keep the full 24 hours.
    public static readonly TimeSpan EmulatorMaxMessageTimeToLive = TimeSpan.FromHours(1);

    public static TimeSpan ClampForEmulator(TimeSpan timeToLive, bool isEmulator) =>
        isEmulator && timeToLive > EmulatorMaxMessageTimeToLive ? EmulatorMaxMessageTimeToLive : timeToLive;
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
