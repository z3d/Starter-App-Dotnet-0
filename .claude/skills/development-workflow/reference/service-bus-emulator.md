# Service Bus Emulator in Aspire

The most fragile part of the local stack. Every failure here presents as something else, which is why the symptoms are listed alongside the causes.

## Never use `WithConfigurationFile()` for topology

It causes container mount and networking issues — the emulator can fail to join the Aspire network and is then unable to reach its own backing database container. Use the fluent API:

```csharp
var serviceBus = builder.AddAzureServiceBus("servicebus");
var topic = serviceBus.AddServiceBusTopic("domain-events");

topic.AddServiceBusSubscription("email-notifications")
    .WithProperties(sub =>
    {
        sub.Rules.Add(new AzureServiceBusRule("MyFilter")
        {
            FilterType = AzureServiceBusFilterType.CorrelationFilter,
            CorrelationFilter = new AzureServiceBusCorrelationFilter
            {
                Properties = { ["EventType"] = "order.created.v1" }
            }
        });
    });

serviceBus.RunAsEmulator(emulator => emulator
    .WithLifetime(ContainerLifetime.Persistent));
```

Do not add a static emulator JSON file as a second topology source.

## Never use `WithConfiguration()` with raw JSON for correlation filters

Aspire's `WithConfiguration(doc => { ... })` serializer maps `CorrelationFilter` through its own schema and emits empty `Properties: {}` in the generated emulator JSON — **even when `ApplicationProperties` is set correctly on the `JsonObject`**. The emulator then rejects the filter with:

> At least one system or user property must be set for a correlation filter.

Use the fluent API above.

## TTLs above 1 hour crash-loop the emulator

The emulator rejects any `DefaultMessageTimeToLive` above one hour:

> Max DefaultMessageTimeToLive supported 1h

The container exits 139. Run mode clamps every TTL through `ServiceBusTopology.ClampForEmulator` while publish mode keeps the deployed 24h posture — this is convention-tested. Never assign the 24h constants to emulator topology directly.

## Exit 139 can also be OOM

The emulator runs its own backing database container (`servicebus-mssql`). On a machine with limited Docker memory — an 8 GB default WSL2 allocation on a 16 GB host, for instance — the pair can be SIGKILLed by the OOM killer.

Symptoms: the emulator starts, creates topics and subscriptions, then dies with `Out of memory`; `docker ps -a` shows `Exited (139)`.

Fix by raising the allocation in `%USERPROFILE%\.wslconfig`:

```ini
[wsl2]
memory=12GB
```

Then `wsl --shutdown` and restart Docker Desktop.

On ARM/Apple Silicon the problem is worse: the emulator image is `linux/amd64` only, so it runs under Rosetta/QEMU with the corresponding memory and CPU overhead.

## Triage order

```bash
# 1. Did it start at all?
docker ps -a
#    "Created"      -> never started
#    "Exited (139)" -> OOM, or a TTL above 1h

# 2. Did it get on the network?
docker inspect <container> --format '{{json .NetworkSettings.Networks}}'
#    "{}" -> network was never attached

# 3. What does it say?
docker logs <container>
#    "SQL DB Unhealthy"                        -> network issue (see step 2)
#    "At least one system or user property..." -> filter serialization (see above)
```

## Recovery from a wedged state

Remove the containers **and** the Aspire network before retrying — a stale network is the most common reason a clean retry fails the same way:

```bash
docker rm -f <containers>
docker network rm <aspire-network>
```

`scripts/reset-servicebus-emulator.sh` does both steps for the emulator pair (finds the two
containers, records their Aspire networks, removes the containers, then the networks). It runs on
the stock macOS bash 3.2; set `CONTAINER_ENGINE=podman` if you are not on Docker.
