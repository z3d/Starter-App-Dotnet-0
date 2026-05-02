using StarterApp.Domain.Abstractions;
using StarterApp.Functions;
using System.Reflection;

namespace StarterApp.AppHost.Tests;

public class ServiceBusTopologyConventionTests
{
    [Fact]
    public void FunctionTriggers_MustTargetConfiguredSubscriptions()
    {
        var triggerBindings = GetServiceBusTriggerBindings().ToList();
        Assert.NotEmpty(triggerBindings);

        Assert.Contains(triggerBindings, binding =>
            binding.FunctionType == typeof(OrderConfirmationEmailFunction) &&
            binding.TopicName == ServiceBusTopology.DomainEventsTopic &&
            binding.SubscriptionName == ServiceBusTopology.EmailNotificationsSubscription &&
            binding.Connection == "servicebus");

        Assert.Contains(triggerBindings, binding =>
            binding.FunctionType == typeof(InventoryReservationFunction) &&
            binding.TopicName == ServiceBusTopology.DomainEventsTopic &&
            binding.SubscriptionName == ServiceBusTopology.InventoryReservationSubscription &&
            binding.Connection == "servicebus");

        var configuredSubscriptions = ServiceBusTopology.SubscriptionFilters
            .Select(filter => filter.SubscriptionName)
            .ToHashSet(StringComparer.Ordinal);

        var unconfiguredTriggers = triggerBindings
            .Where(binding => !configuredSubscriptions.Contains(binding.SubscriptionName))
            .Select(binding => $"{binding.FunctionType.Name} listens to '{binding.SubscriptionName}', but AppHost has no matching subscription filter.")
            .ToList();

        Assert.True(unconfiguredTriggers.Count == 0,
            "Function Service Bus triggers must target subscriptions configured by AppHost:\n" +
            string.Join("\n", unconfiguredTriggers));

        var triggerSubscriptions = triggerBindings
            .Select(binding => binding.SubscriptionName)
            .ToHashSet(StringComparer.Ordinal);

        var subscriptionsWithoutTriggers = configuredSubscriptions
            .Where(subscription => !triggerSubscriptions.Contains(subscription))
            .Select(subscription => $"AppHost configures subscription '{subscription}', but no Function trigger listens to it.")
            .ToList();

        Assert.True(subscriptionsWithoutTriggers.Count == 0,
            "Configured Service Bus subscriptions must have a Function trigger:\n" +
            string.Join("\n", subscriptionsWithoutTriggers));
    }

    [Fact]
    public void AppHostSubscriptions_MustRouteOrderCreatedToCurrentSubscribers()
    {
        var filtersBySubscription = ServiceBusTopology.SubscriptionFilters
            .GroupBy(filter => filter.SubscriptionName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(filter => filter.EventType).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        Assert.Contains(ServiceBusTopology.OrderCreatedEventType,
            filtersBySubscription[ServiceBusTopology.EmailNotificationsSubscription]);

        Assert.Contains(ServiceBusTopology.OrderCreatedEventType,
            filtersBySubscription[ServiceBusTopology.InventoryReservationSubscription]);
    }

    [Fact]
    public void AppHostSubscriptionFilters_MustReferenceDomainEventContracts()
    {
        var domainEventContracts = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(type))
            .Select(type => (System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type) as IDomainEvent)?.EventType)
            .Where(eventType => !string.IsNullOrWhiteSpace(eventType))
            .ToHashSet(StringComparer.Ordinal);

        var unknownContracts = ServiceBusTopology.SubscriptionFilters
            .Where(filter => !domainEventContracts.Contains(filter.EventType))
            .Select(filter => $"{filter.SubscriptionName}/{filter.RuleName} filters on '{filter.EventType}', but no IDomainEvent exposes that contract.")
            .ToList();

        Assert.True(unknownContracts.Count == 0,
            "AppHost Service Bus filters must reference stable domain event contracts:\n" +
            string.Join("\n", unknownContracts));
    }

    private static IEnumerable<ServiceBusTriggerBinding> GetServiceBusTriggerBindings()
    {
        return typeof(OrderConfirmationEmailFunction).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(OrderConfirmationEmailFunction).Namespace)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(method => method.GetParameters()
                    .SelectMany(parameter => parameter.GetCustomAttributes()
                        .Where(attribute => attribute.GetType().Name == "ServiceBusTriggerAttribute")
                        .Select(attribute => new ServiceBusTriggerBinding(
                            type,
                            GetAttributeString(attribute, "TopicName"),
                            GetAttributeString(attribute, "SubscriptionName"),
                            GetAttributeString(attribute, "Connection"))))));
    }

    private static string GetAttributeString(object attribute, string propertyName)
    {
        var value = attribute.GetType().GetProperty(propertyName)?.GetValue(attribute) as string;
        return value ?? string.Empty;
    }

    private sealed record ServiceBusTriggerBinding(
        Type FunctionType,
        string TopicName,
        string SubscriptionName,
        string Connection);
}
