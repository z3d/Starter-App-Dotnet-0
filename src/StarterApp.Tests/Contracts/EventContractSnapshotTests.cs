using System.Text.Json.Nodes;

namespace StarterApp.Tests.Contracts;

// Pins the serialized wire shape of every versioned event contract. The payload is produced
// through OutboxMessage.Create — the exact production path — so a property rename, removal,
// reorder, or serializer-settings change on an event class fails here with a readable diff
// instead of silently breaking Service Bus subscribers and diverging from archived payloads.
// To update deliberately: UPDATE_EVENT_SNAPSHOTS=1 dotnet test --filter EventContractSnapshot,
// review the fixture diff, and decide between a compatible change and a new .v2 contract.
public class EventContractSnapshotTests
{
    private static readonly Assembly DomainAssembly = typeof(Order).Assembly;

    // Property VALUES that legitimately differ per run (timestamps). Names are still pinned:
    // renaming a volatile property surfaces in the diff as a missing + unexpected key.
    private static readonly string[] VolatileProperties = ["OccurredOnUtc", "LastUpdated"];

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, Func<IDomainEvent>> RepresentativeEvents =
        new Dictionary<string, Func<IDomainEvent>>
        {
            [OrderCreatedDomainEvent.Contract] = () => new OrderCreatedDomainEvent(CreateRepresentativeOrder()),
            [OrderStatusChangedDomainEvent.Contract] = () => new OrderStatusChangedDomainEvent(
                CreateRepresentativeOrder(), OrderStatus.Pending, OrderStatus.Confirmed),
        };

    private static Order CreateRepresentativeOrder()
    {
        var order = new Order(new Guid("00000000-0000-0000-0000-000000000001"), 42, "snapshot-owner", "snapshot-tenant");
        order.AddItem(7, "Snapshot Product", 2, Money.Create(19.99m, "USD"));
        return order;
    }

    [Fact]
    public void EveryDomainEvent_HasARepresentativeInstanceAndSnapshot()
    {
        var contracts = DomainEventContracts();

        var missing = contracts.Values
            .Where(contract => !RepresentativeEvents.ContainsKey(contract))
            .OrderBy(c => c)
            .ToList();

        Assert.True(missing.Count == 0,
            "Every domain event contract needs a representative instance in EventContractSnapshotTests.RepresentativeEvents " +
            "and a pinned snapshot (generate with UPDATE_EVENT_SNAPSHOTS=1). Missing:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void SnapshotDirectory_HasNoOrphanFixtures()
    {
        var contracts = DomainEventContracts().Values.ToHashSet(StringComparer.Ordinal);
        var orphans = Directory.EnumerateFiles(SnapshotDirectory(), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !contracts.Contains(name))
            .OrderBy(name => name)
            .ToList();

        Assert.True(orphans.Count == 0,
            "Snapshot fixtures exist for contracts no domain event declares — delete them or restore the event:\n" +
            string.Join("\n", orphans));
    }

    [Fact]
    public void EveryEventContract_MatchesItsPinnedSnapshot()
    {
        var failures = new List<string>();
        var updateMode = Environment.GetEnvironmentVariable("UPDATE_EVENT_SNAPSHOTS") == "1";

        foreach (var (contract, factory) in RepresentativeEvents.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            var domainEvent = factory();
            Assert.Equal(contract, domainEvent.EventType);

            var actual = RenderSnapshot(domainEvent);
            var fixturePath = Path.Combine(SnapshotDirectory(), contract + ".json");

            if (updateMode)
            {
                File.WriteAllText(fixturePath, actual);
                continue;
            }

            if (!File.Exists(fixturePath))
            {
                failures.Add($"{contract}: no pinned snapshot at {fixturePath}. Generate it with UPDATE_EVENT_SNAPSHOTS=1, review, and commit.");
                continue;
            }

            var expected = File.ReadAllText(fixturePath);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{contract}: serialized shape differs from the pinned snapshot. A property rename/removal/reorder " +
                    "or serializer change alters the wire payload under the SAME contract id, breaking subscribers and " +
                    "diverging from archived payloads. Either make the change compatible, or introduce a new .v2 contract. " +
                    "If the change is deliberate, regenerate with UPDATE_EVENT_SNAPSHOTS=1 and justify it in the PR.\n" +
                    $"--- pinned ---\n{expected}\n--- actual ---\n{actual}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    private static string RenderSnapshot(IDomainEvent domainEvent)
    {
        var message = OutboxMessage.Create(domainEvent);
        var node = JsonNode.Parse(message.Payload)!.AsObject();

        foreach (var name in VolatileProperties)
        {
            if (node.ContainsKey(name))
                node[name] = "<normalized>";
        }

        return node.ToJsonString(IndentedOptions) + "\n";
    }

    private static Dictionary<Type, string> DomainEventContracts()
    {
        return DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(t))
            .ToDictionary(
                t => t,
                t => (string?)t.GetField("Contract", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue()
                     ?? throw new InvalidOperationException($"{t.Name} has no public const Contract field."));
    }

    private static string SnapshotDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StarterApp.slnx")))
            {
                var snapshots = Path.Combine(directory.FullName, "src", "StarterApp.Tests", "Contracts", "snapshots");
                Directory.CreateDirectory(snapshots);
                return snapshots;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
    }
}
