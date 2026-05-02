namespace StarterApp.AppHost;

public static class ServiceBusTopology
{
    public const string DomainEventsTopic = "domain-events";
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
