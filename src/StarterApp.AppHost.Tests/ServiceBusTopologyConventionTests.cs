using System.Reflection;
using StarterApp.Domain.Abstractions;
using StarterApp.Functions;

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
    public void Topology_MustNotSilentlyExpireEventsDuringConsumerDowntime()
    {
        // A 1h TTL with expiration dead-lettering off (the Azure default) deletes every event that
        // outlives a subscriber outage with no trace, while the outbox row is already marked
        // processed. Expired events must dead-letter for replay, and the TTL must absorb at least
        // an overnight consumer outage.
        Assert.True(ServiceBusTopology.SubscriptionDeadLetteringOnMessageExpiration,
            "Subscriptions must dead-letter expired messages; otherwise consumer downtime silently destroys events.");
        Assert.True(ServiceBusTopology.SubscriptionDefaultMessageTimeToLive >= TimeSpan.FromHours(24),
            "Subscription TTL must be at least 24h so a subscriber outage does not expire live events.");
        Assert.True(ServiceBusTopology.DomainEventsDefaultMessageTimeToLive >= ServiceBusTopology.SubscriptionDefaultMessageTimeToLive,
            "Topic TTL caps the effective message TTL; it must not undercut the subscription TTL.");
    }

    [Fact]
    public void EmulatorClamp_MustCapTtlAtEmulatorMaximum()
    {
        // The Service Bus emulator crash-loops on any TTL above 1 hour ("Max DefaultMessageTimeToLive
        // supported 1h", container exit 139), so run mode must clamp the deployed 24h posture while
        // publish mode keeps it.
        Assert.Equal(ServiceBusTopology.EmulatorMaxMessageTimeToLive,
            ServiceBusTopology.ClampForEmulator(ServiceBusTopology.SubscriptionDefaultMessageTimeToLive, isEmulator: true));
        Assert.Equal(ServiceBusTopology.SubscriptionDefaultMessageTimeToLive,
            ServiceBusTopology.ClampForEmulator(ServiceBusTopology.SubscriptionDefaultMessageTimeToLive, isEmulator: false));
        Assert.Equal(TimeSpan.FromMinutes(5),
            ServiceBusTopology.ClampForEmulator(TimeSpan.FromMinutes(5), isEmulator: true));
    }

    [Fact]
    public void AppHostSubscriptions_MustApplyTopologyLifecycleConstants()
    {
        // The fluent AppHost config is the deployed topology. Pin it to the ServiceBusTopology
        // constants so the lifecycle posture above cannot drift via inline literals in Program.cs.
        var programSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "StarterApp.AppHost", "Program.cs"));

        var subscriptionBlocks = programSource
            .Split("AddServiceBusSubscription", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(block => block.Split("RunAsEmulator", 2)[0])
            .ToList();

        Assert.Equal(2, subscriptionBlocks.Count);

        foreach (var block in subscriptionBlocks)
        {
            Assert.Contains("ServiceBusTopology.SubscriptionDefaultMessageTimeToLive", block, StringComparison.Ordinal);
            Assert.Contains("ServiceBusTopology.SubscriptionLockDuration", block, StringComparison.Ordinal);
            Assert.Contains("ServiceBusTopology.SubscriptionMaxDeliveryCount", block, StringComparison.Ordinal);
            Assert.Contains("ServiceBusTopology.SubscriptionDeadLetteringOnMessageExpiration", block, StringComparison.Ordinal);
            Assert.DoesNotContain("TimeSpan.From", block, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Repository root not found from test execution directory.");
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
