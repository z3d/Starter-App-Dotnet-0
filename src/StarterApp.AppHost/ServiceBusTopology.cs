namespace StarterApp.AppHost;

public static class ServiceBusTopology
{
    public const string DomainEventsTopic = "domain-events";
    public const bool DomainEventsRequiresDuplicateDetection = true;

    // Retain events for 24 hours to cover overnight subscriber outages.
    // Dead-letter expired events for replay; otherwise they disappear after the outbox
    // has already marked them as published.
    public static readonly TimeSpan DomainEventsDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan DomainEventsDuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SubscriptionDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan SubscriptionLockDuration = TimeSpan.FromSeconds(30);
    public const int SubscriptionMaxDeliveryCount = 5;
    public const bool SubscriptionDeadLetteringOnMessageExpiration = true;

    // The emulator rejects TTLs over one hour at startup. Clamp only in emulator mode;
    // published Azure resources keep the full 24-hour TTL.
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
