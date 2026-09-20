using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ServiceBus;
using StarterApp.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// Seq for local log browsing. Run mode only: it has no identity story (unauthenticated), and a
// deployed environment already gets every log and trace through OTLP to the Aspire dashboard and
// the Container Apps environment's Log Analytics workspace.
IResourceBuilder<SeqResource>? seq = builder.ExecutionContext.IsRunMode
    ? builder.AddSeq("seq").WithLifetime(ContainerLifetime.Persistent)
    : null;

// Publish mode targets Azure Container Apps; every project, container and Dockerfile resource
// below becomes a container app in this one environment (the hosting environment runs the deploy).
builder.AddAzureContainerAppEnvironment("aca");

// A deployed instance is a TEST environment: its Keycloak ships well-known dev users, so every
// public ingress is allow-listed to the operator's address (a CIDR the hosting environment
// supplies; "0.0.0.0/0" would open it). Declared here, not applied afterwards by hand, because the
// deployer rewrites the container apps' ingress on every run.
var operatorCidr = builder.ExecutionContext.IsPublishMode
    ? builder.AddParameter("operator-cidr")                // must be supplied; no value means no deploy
    : builder.AddParameter("operator-cidr", "127.0.0.1/32"); // unused locally; keeps the rig free of prompts
void RestrictIngressToOperator(AzureResourceInfrastructure infra, ContainerApp app) =>
    app.Configuration.Ingress.IPSecurityRestrictions.Add(new ContainerAppIPSecurityRestrictionRule
    {
        Name = "operator",
        Action = ContainerAppIPRuleAction.Allow,
        IPAddressRange = operatorCidr.AsProvisioningParameter(infra),
        Description = "The operator's address; the dev realm's credentials are well known.",
    });

// PostgreSQL: a container locally, Azure Database for PostgreSQL (flexible server) when
// published. The Azure server is Entra-only, so the deployed connection string names each app's
// managed identity and carries no password — the shape DatabaseAuthentication treats as "the
// hosting identity's token". Locally the container's generated password takes the password path.
var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
                      .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent));

var db = postgres.AddDatabase("database");

// Redis for distributed caching: a container locally, Azure Managed Redis when published, with
// Entra authentication (each referencing app's managed identity gets an access policy; no
// access key exists). The Aspire client integration the API uses handles the token exchange.
var redis = builder.AddAzureManagedRedis("redis")
                   .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent));

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
// every environment exercises the same discovery -> JWKS -> verify path. The realm and admin
// bootstrap ship well-known development credentials by design, which is why a deployed instance
// is only ever a TEST environment with its ingress restricted to the operator's addresses (the
// hosting environment repo owns that restriction). Plain container rather than
// Aspire.Hosting.Keycloak: that package has no stable release, and this repo does not take
// preview dependencies.
//
// Locally the realm is bind-mounted; a container app cannot mount a repo directory, so publish
// mode builds a tiny image (Realms/Dockerfile) with the realm copied in. Both run
// `--import-realm` against Keycloak's dev-mode in-memory store: nothing to migrate, nothing to
// back up, and the realm is re-imported on every start.
IResourceBuilder<IResourceWithEndpoints> keycloak;
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
else
{
    // The admin console password is generated once per environment (persisted in the deployer's
    // state, never prompted for) and lives only as a container app secret — nothing about the
    // deployed instance is a literal in this repository.
    var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password",
        new GenerateParameterDefault { MinLength = 32 }, secret: true, persist: true);
    keycloak = builder.AddDockerfile("keycloak", "Realms")
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithExternalHttpEndpoints()
        .PublishAsAzureContainerApp(RestrictIngressToOperator)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        // TLS terminates at the container app ingress; Keycloak derives its public hostname and
        // https scheme (and so the token issuer) from the forwarded headers.
        .WithEnvironment("KC_HTTP_ENABLED", "true")
        .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
        .WithEnvironment("KC_HOSTNAME_STRICT", "false")
        .WithArgs("start-dev", "--import-realm");
}

// Add the database migrator as a separate service (must complete before API starts)
var migrator = builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
       // Published from the project's own Dockerfile (the one CI validates) rather than the SDK's
       // container publish, which restores for linux-x64 and so cannot use the locked lock files.
       .PublishAsDockerFile(container => container.WithDockerfile(repoRoot, "src/StarterApp.DbMigrator/Dockerfile"))
       // A run-to-completion process is a Container App *job*, not an app that would be restarted
       // forever; the hosting environment starts it after each deploy (or runs the migrator
       // locally against the server with the operator's own Entra identity).
       .PublishAsAzureContainerAppJob((_, job) => job.Configuration.TriggerType = Azure.Provisioning.AppContainers.ContainerAppJobTriggerType.Manual)
       .WithReference(db)
       .WaitFor(db);

// Add the API project with reference to the database and Service Bus
var api = builder.AddProject<Projects.StarterApp_Api>("api")
       .PublishAsDockerFile(container => container.WithDockerfile(repoRoot, "src/StarterApp.Api/Dockerfile"))
       .WithReference(db)
       .WithReference(redis)
       .WithReference(payloadArchive)
       .WithReference(serviceBus)
       .WithEnvironment("PayloadCapture__RequireArchiveStore", "true")
       .WithEnvironment("PayloadCapture__ServiceBusFailureMode", "FailClosed")
       // Behind the container app ingress TLS terminates upstream; this is ASP.NET Core's switch
       // for honouring X-Forwarded-Proto/For, so UseHttpsRedirection does not loop.
       .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
       .WithExternalHttpEndpoints()
       .PublishAsAzureContainerApp(RestrictIngressToOperator)
       .WaitFor(db)
       .WaitFor(redis)
       .WaitFor(payloadArchive)
       .WaitFor(serviceBus)
       .WaitForCompletion(migrator);

if (seq is not null)
{
    migrator.WithEnvironment("SEQ_URL", seq.GetEndpoint("http")).WaitFor(seq);
    api.WithEnvironment("SEQ_URL", seq.GetEndpoint("http")).WaitFor(seq);
}

// Point the API at the Keycloak realm. Locally the container speaks plain http, so
// RequireHttpsMetadata=false (options validation rejects it outside Development/Testing); when
// published the endpoint reference resolves to the container app's https ingress.
api.WithEnvironment("Identity__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/starterapp"))
   .WaitFor(keycloak);
if (builder.ExecutionContext.IsRunMode)
    api.WithEnvironment("Identity__RequireHttpsMetadata", "false");

// Add Azure Functions container for Service Bus subscribers.
// Running through the Functions base image keeps local behavior aligned with the deployed worker runtime.
var functions = builder.AddDockerfile("functions", repoRoot, "src/StarterApp.Functions/Dockerfile")
       // The Functions host serves a landing page on port 80 once the worker is up; exposing it
       // lets the E2E fixture (and the dashboard) verify the slowest resource is actually ready —
       // the API's readiness probe says nothing about the subscriber container.
       .WithHttpEndpoint(targetPort: 80)
       .WithReference(payloadArchive)
       .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
       // The Functions image logs the host's own lines (startup, trigger listeners, invocations)
       // to the console only when asked; without this a deployed subscriber is a black box.
       .WithEnvironment("AzureFunctionsJobHost__Logging__Console__IsEnabled", "true")
       .WithEnvironment(context =>
       {
           ((IResourceWithAzureFunctionsConfig)serviceBus.Resource).ApplyAzureFunctionsConfiguration(context.EnvironmentVariables, "servicebus");
           ((IResourceWithAzureFunctionsConfig)storage.Resource).ApplyAzureFunctionsConfiguration(context.EnvironmentVariables, "AzureWebJobsStorage");
       })
       .WithEnvironment("ConnectionStrings__payloadarchive", payloadArchive.Resource.ConnectionStringExpression)
       // Job-run history (job_runs table): the cleanup function records its runs durably.
       .WithEnvironment("ConnectionStrings__database", db.Resource.ConnectionStringExpression)
       .WithEnvironment("PayloadCapture__RequireArchiveStore", "true")
       .WithEnvironment("PayloadCapture__ServiceBusFailureMode", "FailClosed")
       .WithEnvironment("PayloadCapture__CleanupCron", "0 0 * * * *")
       .WaitFor(serviceBus)
       .WaitFor(payloadArchive);

// The emulator only speaks a keyed connection string, which the Functions host reads from the
// bare `servicebus` setting; published, ApplyAzureFunctionsConfiguration above emits
// servicebus__fullyQualifiedNamespace for the managed identity instead, and a bare
// `servicebus` holding an endpoint would shadow it.
if (builder.ExecutionContext.IsRunMode)
{
    functions.WithReference(serviceBus)
             .WithEnvironment("servicebus", serviceBus.Resource.ConnectionStringExpression);
}
else
{
    // Deployed, the trigger connection must resolve to identity settings only. The Functions host
    // looks at ConnectionStrings:servicebus *before* servicebus__fullyQualifiedNamespace, so a
    // WithReference (which injects the endpoint URL there) makes it try to parse a URL as a
    // connection string and the listeners never start. The reference's other job — the role
    // assignment — is requested explicitly instead. And ApplyAzureFunctionsConfiguration fills
    // servicebus__fullyQualifiedNamespace with the endpoint URL where the extension wants the
    // bare host; the module already computes that host (serviceBusHostName).
    functions.WithRoleAssignments(serviceBus, ServiceBusBuiltInRole.AzureServiceBusDataOwner)
             .WithEnvironment("servicebus__fullyQualifiedNamespace", serviceBus.GetOutput("serviceBusHostName"));
}

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
