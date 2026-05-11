using StarterApp.Domain.Abstractions;
using StarterApp.Functions;
using System.Reflection;
using System.Text.Json;

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

    [Fact]
    public void ServiceBusEmulatorTopicProperties_MustMatchAppHostTopology()
    {
        var configPath = Path.Combine(FindRepoRoot(), "config", "servicebus-emulator.json");
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));

        var topics = document.RootElement
            .GetProperty("UserConfig")
            .GetProperty("Namespaces")[0]
            .GetProperty("Topics")
            .EnumerateArray();

        var topic = topics.Single(topicElement =>
            topicElement.GetProperty("Name").GetString() == ServiceBusTopology.DomainEventsTopic);
        var properties = topic.GetProperty("Properties");

        Assert.Equal(ServiceBusTopology.DomainEventsRequiresDuplicateDetection,
            properties.GetProperty("RequiresDuplicateDetection").GetBoolean());
        Assert.Equal(ToIso8601Duration(ServiceBusTopology.DomainEventsDefaultMessageTimeToLive),
            properties.GetProperty("DefaultMessageTimeToLive").GetString());
        Assert.Equal(ToIso8601Duration(ServiceBusTopology.DomainEventsDuplicateDetectionHistoryTimeWindow),
            properties.GetProperty("DuplicateDetectionHistoryTimeWindow").GetString());
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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StarterApp.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string ToIso8601Duration(TimeSpan value)
    {
        if (value.TotalHours >= 1 && value == TimeSpan.FromHours(Math.Truncate(value.TotalHours)))
            return $"PT{value.TotalHours:0}H";

        if (value.TotalMinutes >= 1 && value == TimeSpan.FromMinutes(Math.Truncate(value.TotalMinutes)))
            return $"PT{value.TotalMinutes:0}M";

        return $"PT{value.TotalSeconds:0}S";
    }

    private sealed record ServiceBusTriggerBinding(
        Type FunctionType,
        string TopicName,
        string SubscriptionName,
        string Connection);
}
