namespace StarterApp.AppHost;

public static class ServiceBusTopology
{
    public const string DomainEventsTopic = "domain-events";
    public const bool DomainEventsRequiresDuplicateDetection = true;

    // A 24 hour TTL covers a subscriber that is down overnight. Expired events go to the dead-letter
    // queue, where they can be replayed. With the Azure default they would be deleted without trace,
    // after the outbox had already marked them published.
    public static readonly TimeSpan DomainEventsDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan DomainEventsDuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SubscriptionDefaultMessageTimeToLive = TimeSpan.FromHours(24);
    public static readonly TimeSpan SubscriptionLockDuration = TimeSpan.FromSeconds(30);
    public const int SubscriptionMaxDeliveryCount = 5;
    public const bool SubscriptionDeadLetteringOnMessageExpiration = true;

    // The Service Bus emulator crash-loops on any TTL over one hour. The clamp only applies to the
    // emulator. The published Azure resources keep the full 24 hours.
    public static readonly TimeSpan EmulatorMaxMessageTimeToLive = TimeSpan.FromHours(1);

    public static TimeSpan ClampForEmulator(TimeSpan timeToLive, bool isEmulator) =>
        isEmulator && timeToLive > EmulatorMaxMessageTimeToLive ? EmulatorMaxMessageTimeToLive : timeToLive;
    public const string EmailNotificationsSubscription = "email-notifications";
    public const string InventoryReservationSubscription = "inventory-reservation";
    // Rule names include the subscription name. Service Bus scopes a rule to its subscription, but the
    // Bicep that Aspire publishes makes every rule a top-level resource, so two subscriptions with the
    // same rule name fail to deploy. ServiceBusTopologyConventionTests checks they are unique.
    public const string OrderCreatedRuleName = "order-created";
    public const string OrderStatusChangedRuleName = "order-status-changed";

    public static readonly IReadOnlyCollection<SubscriptionFilter> SubscriptionFilters =
    [
        new(EmailNotificationsSubscription, $"{EmailNotificationsSubscription}-{OrderCreatedRuleName}", OrderCreatedEventType),
        new(EmailNotificationsSubscription, $"{EmailNotificationsSubscription}-{OrderStatusChangedRuleName}", OrderStatusChangedEventType),
        new(InventoryReservationSubscription, $"{InventoryReservationSubscription}-{OrderCreatedRuleName}", OrderCreatedEventType)
    ];

    public const string OrderCreatedEventType = "order.created.v1";
    public const string OrderStatusChangedEventType = "order.status-changed.v1";
}

public sealed record SubscriptionFilter(string SubscriptionName, string RuleName, string EventType);
