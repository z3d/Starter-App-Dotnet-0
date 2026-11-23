using System.Reflection;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Npgsql;

namespace StarterApp.Functions;

// What the worker needs before it can do its work: each subscription it reads, and the database. A subscription the worker's identity cannot read, or a database it cannot reach, otherwise shows only in the trigger's log while the container looks well.
public sealed class WorkerHealth
{
    public const string Healthy = "healthy";

    // A probe waits no longer than this for one check; the Service Bus client's own wait is a minute.
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<(string Name, Func<CancellationToken, Task> Check)> _checks;
    private readonly TimeSpan _timeout;

    internal WorkerHealth(IReadOnlyList<(string Name, Func<CancellationToken, Task> Check)> checks, TimeSpan timeout)
    {
        _checks = checks;
        _timeout = timeout;
    }

    public static WorkerHealth For(ServiceBusClient? serviceBus, NpgsqlDataSource? database)
    {
        var checks = new List<(string, Func<CancellationToken, Task>)>();

        foreach (var (topic, subscription) in Subscriptions(typeof(WorkerHealth).Assembly))
        {
            checks.Add(($"servicebus-{topic}-{subscription}", async cancellationToken =>
            {
                if (serviceBus is null)
                    throw new InvalidOperationException("The worker has no Service Bus setting.");

                // Peeking needs the right to listen and takes no message.
                await using var receiver = serviceBus.CreateReceiver(topic, subscription);
                await receiver.PeekMessageAsync(cancellationToken: cancellationToken);
            }
            ));
        }

        checks.Add(("database", async cancellationToken =>
        {
            if (database is null)
                throw new InvalidOperationException("The worker has no database connection string.");

            await using var connection = await database.OpenConnectionAsync(cancellationToken);
        }
        ));

        return new WorkerHealth(checks, CheckTimeout);
    }

    // Read from the triggers themselves, so a new subscriber is checked without a list to keep.
    internal static IReadOnlyList<(string Topic, string Subscription)> Subscriptions(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.GetCustomAttribute<ServiceBusTriggerAttribute>())
            .Where(trigger => trigger is { TopicName.Length: > 0, SubscriptionName.Length: > 0 })
            .Select(trigger => (trigger!.TopicName!, trigger.SubscriptionName!))
            .Distinct()
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal).ThenBy(pair => pair.Item2, StringComparer.Ordinal)
            .ToList();

    public async Task<IReadOnlyDictionary<string, string>> CheckAsync(CancellationToken cancellationToken)
    {
        var statuses = await Task.WhenAll(_checks.Select(check => RunAsync(check.Check, cancellationToken)));
        return _checks.Select((check, index) => (check.Name, Status: statuses[index]))
            .ToDictionary(pair => pair.Name, pair => pair.Status, StringComparer.Ordinal);
    }

    public static bool IsHealthy(IReadOnlyDictionary<string, string> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        return checks.Values.All(status => status == Healthy);
    }

    // Only the exception's type is given: the address takes no sign-in, and a message can name hosts or principals.
    private async Task<string> RunAsync(Func<CancellationToken, Task> check, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await check(linked.Token).WaitAsync(linked.Token);
            return Healthy;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return "unhealthy (timed out)";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return $"unhealthy ({exception.GetType().Name})";
        }
    }
}
