using System.Security.Cryptography;
using Aspire.Hosting.Azure;
using StarterApp.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// Add Seq for centralized logging
var seq = builder.AddSeq("seq")
                 .WithLifetime(ContainerLifetime.Persistent);

// Add PostgreSQL with persistent lifetime
var postgres = builder.AddPostgres("postgres")
                      .WithLifetime(ContainerLifetime.Persistent);

var db = postgres.AddDatabase("database");

// Add Redis for distributed caching
var redis = builder.AddRedis("redis")
                   .WithLifetime(ContainerLifetime.Persistent);

// Add Azure Blob Storage emulator for payload archive and audit artifacts
var storage = builder.AddAzureStorage("storage");
var payloadArchive = storage.AddBlobs("payloadarchive");

storage.RunAsEmulator(emulator => emulator
    .WithLifetime(ContainerLifetime.Persistent));

// Add Azure Service Bus emulator for domain event messaging
// Topology defined via fluent API so Aspire serializes correlation filters correctly
var serviceBus = builder.AddAzureServiceBus("servicebus");

// Run mode targets the emulator, which crash-loops on any TTL above ServiceBusTopology's
// 1-hour emulator maximum; publish mode keeps the deployed 24h no-event-silently-lost posture.
var isEmulator = builder.ExecutionContext.IsRunMode;

var domainEventsTopic = serviceBus.AddServiceBusTopic(ServiceBusTopology.DomainEventsTopic)
    .WithProperties(topic =>
    {
        topic.DefaultMessageTimeToLive = ServiceBusTopology.ClampForEmulator(ServiceBusTopology.DomainEventsDefaultMessageTimeToLive, isEmulator);
        topic.RequiresDuplicateDetection = ServiceBusTopology.DomainEventsRequiresDuplicateDetection;
        topic.DuplicateDetectionHistoryTimeWindow = ServiceBusTopology.DomainEventsDuplicateDetectionHistoryTimeWindow;
    });

domainEventsTopic.AddServiceBusSubscription(ServiceBusTopology.EmailNotificationsSubscription)
    .WithProperties(sub =>
    {
        sub.DefaultMessageTimeToLive = ServiceBusTopology.ClampForEmulator(ServiceBusTopology.SubscriptionDefaultMessageTimeToLive, isEmulator);
        sub.LockDuration = ServiceBusTopology.SubscriptionLockDuration;
        sub.MaxDeliveryCount = ServiceBusTopology.SubscriptionMaxDeliveryCount;
        sub.DeadLetteringOnMessageExpiration = ServiceBusTopology.SubscriptionDeadLetteringOnMessageExpiration;
        foreach (var filter in ServiceBusTopology.SubscriptionFilters.Where(filter =>
                     filter.SubscriptionName == ServiceBusTopology.EmailNotificationsSubscription))
            sub.Rules.Add(new AzureServiceBusRule(filter.RuleName)
            {
                FilterType = AzureServiceBusFilterType.CorrelationFilter,
                CorrelationFilter = new AzureServiceBusCorrelationFilter
                {
                    Properties = { ["EventType"] = filter.EventType }
                }
            });
    });

domainEventsTopic.AddServiceBusSubscription(ServiceBusTopology.InventoryReservationSubscription)
    .WithProperties(sub =>
    {
        sub.DefaultMessageTimeToLive = ServiceBusTopology.ClampForEmulator(ServiceBusTopology.SubscriptionDefaultMessageTimeToLive, isEmulator);
        sub.LockDuration = ServiceBusTopology.SubscriptionLockDuration;
        sub.MaxDeliveryCount = ServiceBusTopology.SubscriptionMaxDeliveryCount;
        sub.DeadLetteringOnMessageExpiration = ServiceBusTopology.SubscriptionDeadLetteringOnMessageExpiration;
        foreach (var filter in ServiceBusTopology.SubscriptionFilters.Where(filter =>
                     filter.SubscriptionName == ServiceBusTopology.InventoryReservationSubscription))
            sub.Rules.Add(new AzureServiceBusRule(filter.RuleName)
            {
                FilterType = AzureServiceBusFilterType.CorrelationFilter,
                CorrelationFilter = new AzureServiceBusCorrelationFilter
                {
                    Properties = { ["EventType"] = filter.EventType }
                }
            });
    });

serviceBus.RunAsEmulator(emulator => emulator
    .WithLifetime(ContainerLifetime.Persistent));

// Add the database migrator as a separate service (must complete before API starts)
var migrator = builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
       .WithReference(db)
       .WithEnvironment("SEQ_URL", seq.GetEndpoint("http"))
       .WaitFor(db)
       .WaitFor(seq);

// Add the API project with reference to the database and Service Bus
var api = builder.AddProject<Projects.StarterApp_Api>("api")
       .WithReference(db)
       .WithReference(redis)
       .WithReference(payloadArchive)
       .WithReference(serviceBus)
       .WithEnvironment("SEQ_URL", seq.GetEndpoint("http"))
       .WithEnvironment("PayloadCapture__RequireArchiveStore", "true")
       .WithEnvironment("PayloadCapture__ServiceBusFailureMode", "FailClosed")
       .WaitFor(db)
       .WaitFor(redis)
       .WaitFor(payloadArchive)
       .WaitFor(seq)
       .WaitFor(serviceBus)
       .WaitForCompletion(migrator);

// Local APIM emulator (opt-in, run mode only): StarterApp.Gateway fronts the API like a trusted
// gateway — strips inbound caller identity, projects normalized X-Authenticated-* headers
// (caller-stated or a default dev identity), and signs the X-Gateway-Assertion. The API flips to
// GatewayIdentity:Mode=Required so local orchestration exercises the production verification path.
// Opt-in (run with `--gateway` or ENABLE_GATEWAY=true) so the default rig and the AppHost.Tests —
// which call the API directly with unsigned projected headers — keep working unchanged. Publish
// mode is untouched: the gateway is a dev-only emulator, never a deployable resource.
var gatewayEnabled = builder.ExecutionContext.IsRunMode &&
    (args.Contains("--gateway") || Environment.GetEnvironmentVariable("ENABLE_GATEWAY") == "true");

if (gatewayEnabled)
{
    // Per-run key, never persisted or committed: assertions live 60 seconds, so invalidating them
    // across AppHost restarts costs nothing, and a committed constant would only be a secret-shaped
    // string to allowlist.
    var gatewaySigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    const string gatewayKeyId = "local-dev-gateway";

    api.WithEnvironment("GatewayIdentity__Mode", "Required")
       .WithEnvironment("GatewayIdentity__SigningKey", gatewaySigningKey)
       .WithEnvironment("GatewayIdentity__KeyId", gatewayKeyId);

    var gateway = builder.AddProject<Projects.StarterApp_Gateway>("gateway")
        .WithReference(api)
        .WithEnvironment("GatewaySigner__SigningKey", gatewaySigningKey)
        .WithEnvironment("GatewaySigner__KeyId", gatewayKeyId)
        .WaitFor(api);

    // Dev Tunnel fronts the gateway (the signed door) when the emulator is enabled.
    if (args.Contains("--devtunnel") || Environment.GetEnvironmentVariable("ENABLE_DEV_TUNNEL") == "true")
    {
        // The gateway emulator trusts caller-stated X-Authenticated-* headers and signs them as a
        // verified identity, so any tunnel caller can claim any identity. Exposing that surface to
        // the internet must be an explicit, acknowledged decision.
        if (Environment.GetEnvironmentVariable("DEV_TUNNEL_ACK_HEADER_TRUST_GATEWAY") != "true")
            throw new InvalidOperationException(
                "Refusing to start the dev tunnel: the gateway emulator trusts caller-stated " +
                "X-Authenticated-* headers and signs them as a verified identity, so any tunnel caller " +
                "can claim any identity. Set DEV_TUNNEL_ACK_HEADER_TRUST_GATEWAY=true to acknowledge " +
                "exposing this surface through the tunnel.");

        builder.AddDevTunnel("gateway-tunnel")
               .WithReference(gateway);
    }
}

// Add Azure Functions container for Service Bus subscribers.
// Running through the Functions base image keeps local behavior aligned with the deployed worker runtime.
builder.AddDockerfile("functions", repoRoot, "src/StarterApp.Functions/Dockerfile")
       // The Functions host serves a landing page on port 80 once the worker is up; exposing it
       // lets the E2E fixture (and the dashboard) verify the slowest resource is actually ready —
       // the API's readiness probe says nothing about the subscriber container.
       .WithHttpEndpoint(targetPort: 80)
       .WithReference(serviceBus)
       .WithReference(payloadArchive)
       .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
       .WithEnvironment(context =>
       {
           ((IResourceWithAzureFunctionsConfig)serviceBus.Resource).ApplyAzureFunctionsConfiguration(context.EnvironmentVariables, "servicebus");
           ((IResourceWithAzureFunctionsConfig)storage.Resource).ApplyAzureFunctionsConfiguration(context.EnvironmentVariables, "AzureWebJobsStorage");
       })
       .WithEnvironment("servicebus", serviceBus.Resource.ConnectionStringExpression)
       .WithEnvironment("ConnectionStrings__payloadarchive", payloadArchive.Resource.ConnectionStringExpression)
       // Job-run history (job_runs table): the cleanup function records its runs durably.
       .WithEnvironment("ConnectionStrings__database", db.Resource.ConnectionStringExpression)
       .WithEnvironment("PayloadCapture__RequireArchiveStore", "true")
       .WithEnvironment("PayloadCapture__ServiceBusFailureMode", "FailClosed")
       .WithEnvironment("PayloadCapture__CleanupCron", "0 0 * * * *")
       .WaitFor(serviceBus)
       .WaitFor(payloadArchive);

// Dev Tunnel: expose the API to the internet for webhook/mobile testing
// Enable with: dotnet run -- --devtunnel  OR  set ENABLE_DEV_TUNNEL=true
// Skipped when the gateway emulator is enabled — that path tunnels the gateway (the signed door) instead.
if (!gatewayEnabled && (args.Contains("--devtunnel") || Environment.GetEnvironmentVariable("ENABLE_DEV_TUNNEL") == "true"))
{
    // The locally-orchestrated API runs GatewayIdentity:Mode=UnsignedDevelopment — it trusts
    // projected identity headers without a signed gateway assertion. Exposing that surface to
    // the internet (even Microsoft-auth-gated dev tunnels) must be an explicit, acknowledged
    // decision, not a side effect of a convenience flag.
    if (Environment.GetEnvironmentVariable("DEV_TUNNEL_ACK_UNSIGNED_API") != "true")
        throw new InvalidOperationException(
            "Refusing to start the dev tunnel: the API runs with GatewayIdentity:Mode=UnsignedDevelopment, " +
            "which trusts identity headers without a signed gateway assertion. Set DEV_TUNNEL_ACK_UNSIGNED_API=true " +
            "to acknowledge exposing this surface through the tunnel.");

    builder.AddDevTunnel("api-tunnel")
           .WithReference(api);
}

// After adding all resources, run the app...
builder.Build().Run();
