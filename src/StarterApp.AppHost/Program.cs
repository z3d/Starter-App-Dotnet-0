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

// Dev IdP: Keycloak with the committed starterapp realm (asymmetric RS256, JWKS published), so
// local dev exercises the same discovery -> JWKS -> verify path as production. Run mode only —
// deployed environments use a real identity provider, so the container must never appear in a
// publish manifest. The realm and admin bootstrap ship well-known development credentials by
// design. Plain container rather than Aspire.Hosting.Keycloak: that package has no stable
// release, and this repo does not take preview dependencies.
IResourceBuilder<ContainerResource>? keycloak = null;
if (builder.ExecutionContext.IsRunMode)
{
    keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.4")
        // Digest-pinned like the Dockerfile base images. Resolve a new digest when bumping:
        // curl -sI https://quay.io/v2/keycloak/keycloak/manifests/<tag> \
        //   -H "Accept: application/vnd.oci.image.index.v1+json"
        .WithImageSHA256("9409c59bdfb65dbffa20b11e6f18b8abb9281d480c7ca402f51ed3d5977e6007")
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithHttpEndpoint(targetPort: 9000, name: "management")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        .WithBindMount("Realms", "/opt/keycloak/data/import", isReadOnly: true)
        .WithArgs("start-dev", "--import-realm")
        .WithHttpHealthCheck("/health/ready", endpointName: "management")
        .WithLifetime(ContainerLifetime.Persistent);
}

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

// Point the API at the dev Keycloak realm. RequireHttpsMetadata=false is dev-only (the local
// container speaks plain http); options validation rejects it outside Development/Testing.
if (keycloak is not null)
{
    api.WithEnvironment("Identity__Authority",
            ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/starterapp"))
       .WithEnvironment("Identity__RequireHttpsMetadata", "false")
       .WaitFor(keycloak);
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
if (args.Contains("--devtunnel") || Environment.GetEnvironmentVariable("ENABLE_DEV_TUNNEL") == "true")
{
    // The tunneled API accepts tokens minted by the local dev Keycloak, whose realm ships
    // well-known development credentials — anyone who can reach the tunnel can mint a valid
    // token. Exposing that surface to the internet (even Microsoft-auth-gated dev tunnels) must
    // be an explicit, acknowledged decision, not a side effect of a convenience flag.
    if (Environment.GetEnvironmentVariable("DEV_TUNNEL_ACK_DEV_IDP") != "true")
        throw new InvalidOperationException(
            "Refusing to start the dev tunnel: the API accepts tokens from the local dev Keycloak, " +
            "whose realm ships well-known development credentials, so anyone reaching the tunnel can " +
            "mint a valid token. Set DEV_TUNNEL_ACK_DEV_IDP=true to acknowledge exposing this surface " +
            "through the tunnel.");

    builder.AddDevTunnel("api-tunnel")
           .WithReference(api);
}

// After adding all resources, run the app...
builder.Build().Run();
