using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ServiceBus;
using StarterApp.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// Seq for browsing logs locally. Run mode only: it has no authentication, and a deployed
// environment already sends every log and trace over OTLP to the Aspire dashboard and Log Analytics.
IResourceBuilder<SeqResource>? seq = builder.ExecutionContext.IsRunMode
    ? builder.AddSeq("seq").WithLifetime(ContainerLifetime.Persistent)
    : null;

// Publish mode targets Azure Container Apps. Every project, container and Dockerfile below becomes
// a container app in this one environment. The hosting repository runs the deploy.
var aca = builder.AddAzureContainerAppEnvironment("aca");

// Container apps call each other on the environment's internal hostnames. The public hostnames sit
// behind the operator allow-list below, which applies to every caller, other apps included. Internal
// names never cross it. Browsers use the public names, and Keycloak pins its issuer to the public
// name so tokens validate either way.
ReferenceExpression Internal(string app) => builder.ExecutionContext.IsPublishMode
    ? ReferenceExpression.Create($"https://{app}.internal.{aca.GetOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN")}")
    : throw new InvalidOperationException("Internal hostnames exist only in a published environment.");

// A deployed instance is a test environment. Its Keycloak ships well-known dev users, so every
// public ingress is limited to the operator's address, a CIDR the hosting repository supplies
// ("0.0.0.0/0" would open it to everyone). It is declared here rather than set by hand afterwards,
// because the deployer rewrites the ingress on every run.
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

// PostgreSQL is a container locally and Azure Database for PostgreSQL (flexible server) when
// published. The Azure server is Entra-only, so the deployed connection string names each app's
// managed identity and has no password. DatabaseAuthentication reads that shape as "use the hosting
// identity's token". Locally the container's generated password is used as given.
var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
                      .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent));

var db = postgres.AddDatabase("database");

// Redis for the distributed cache: a container locally, Azure Managed Redis when published. The
// Azure one uses Entra. Each app's managed identity gets an access policy and there is no access
// key. The Aspire client integration in the API exchanges the token.
var redis = builder.AddAzureManagedRedis("redis")
                   .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent));

var storage = builder.AddAzureStorage("storage");
var payloadArchive = storage.AddBlobs("payloadarchive");

storage.RunAsEmulator(emulator => emulator
    .WithLifetime(ContainerLifetime.Persistent));

// The topology uses the fluent API because Aspire only writes correlation filters out correctly from that form.
var serviceBus = builder.AddAzureServiceBus("servicebus");

// Run mode uses the emulator, which crash-loops on any TTL over one hour. Publish mode keeps the
// full 24 hours so no event is lost without trace.
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

// The dev identity provider is Keycloak with the committed starterapp realm (RS256, JWKS
// published), so every environment goes through the same discovery, JWKS and verify path. The realm
// and admin bootstrap ship well-known dev credentials on purpose. That is why a deployed instance is
// only ever a test environment, with its ingress limited to the operator's addresses by the hosting
// repository. It is a plain container rather than Aspire.Hosting.Keycloak because that package has
// no stable release and this repo takes no preview dependencies.
//
// Locally the realm is bind-mounted. A container app cannot mount a repo directory, so publish mode
// builds a small image (Realms/Dockerfile) with the realm copied in. Both run --import-realm against
// Keycloak's in-memory dev store: nothing to migrate or back up, and the realm is re-imported on
// every start.
IResourceBuilder<IResourceWithEndpoints> keycloak;
if (builder.ExecutionContext.IsRunMode)
{
    keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.4")
        // Pinned by digest like the Dockerfile base images. To bump it, get the new digest with:
        // curl -sI https://quay.io/v2/keycloak/keycloak/manifests/<tag> \
        //   -H "Accept: application/vnd.oci.image.index.v1+json"
        .WithImageSHA256("9409c59bdfb65dbffa20b11e6f18b8abb9281d480c7ca402f51ed3d5977e6007")
        .WithHttpEndpoint(port: 8090, targetPort: 8080, name: "http")
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
    // The realm's users have well-known passwords and a fixed amr=mfa attribute. A derived project must never deploy it as its real identity provider.
    if (Environment.GetEnvironmentVariable("DEPLOY_ACK_DEV_IDP") != "true")
        throw new InvalidOperationException(
            "Refusing to publish: the deployed Keycloak is the dev realm, whose well-known users mint valid tokens and whose " +
            "static amr attribute satisfies every MFA requirement. Set DEPLOY_ACK_DEV_IDP=true to acknowledge this is a test " +
            "environment, or replace the keycloak resource with your real identity provider.");

    // The admin console password is generated once per environment, kept in the deployer's state and
    // stored only as a container app secret. Nothing about the deployed instance is written down in
    // this repository.
    var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password",
        new GenerateParameterDefault { MinLength = 32 }, secret: true, persist: true);
    var keycloakContainer = builder.AddDockerfile("keycloak", "Realms")
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithExternalHttpEndpoints()
        .PublishAsAzureContainerApp(RestrictIngressToOperator)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        // TLS ends at the container app ingress, so Keycloak takes the https scheme from the forwarded
        // headers. The issuer is pinned to the public name whatever hostname the caller used. The API
        // reaches Keycloak on the internal name and still needs metadata and tokens that carry the issuer
        // it accepts.
        .WithEnvironment("KC_HTTP_ENABLED", "true")
        .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
        .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true")
        .WithArgs("start-dev", "--import-realm");
    keycloakContainer.WithEnvironment("KC_HOSTNAME", keycloakContainer.GetEndpoint("http"));
    keycloak = keycloakContainer;
}

var migrator = builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
       // Published from the project's own Dockerfile, the one CI checks. The SDK's container publish
       // restores for linux-x64 and cannot use the locked lock files.
       .PublishAsDockerFile(container => container.WithDockerfile(repoRoot, "src/StarterApp.DbMigrator/Dockerfile"))
       // A process that runs to completion is a Container Apps job, not an app that would be restarted
       // forever. The hosting repository starts it after each deploy, or runs the migrator locally with
       // the operator's own Entra identity.
       .PublishAsAzureContainerAppJob((_, job) => job.Configuration.TriggerType = Azure.Provisioning.AppContainers.ContainerAppJobTriggerType.Manual)
       .WithReference(db)
       .WaitFor(db);

var api = builder.AddProject<Projects.StarterApp_Api>("api")
       .PublishAsDockerFile(container => container.WithDockerfile(repoRoot, "src/StarterApp.Api/Dockerfile"))
       .WithReference(db)
       .WithReference(redis)
       .WithReference(payloadArchive)
       .WithReference(serviceBus)
       .WithEnvironment("PayloadCapture__RequireArchiveStore", "true")
       .WithEnvironment("PayloadCapture__ServiceBusFailureMode", "FailClosed")
       // TLS ends at the container app ingress. This switch makes ASP.NET Core honour X-Forwarded-Proto
       // and X-Forwarded-For, so UseHttpsRedirection does not loop.
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

// Points the API at the Keycloak realm. Locally the container speaks plain http, so
// RequireHttpsMetadata is false. Options validation rejects that outside Development and Testing.
// Identity:Authority is both the issuer tokens must carry and where discovery is fetched. When
// published, discovery at the public name would have to cross the allow-list, so the API gets the
// public issuer to validate and the internal name to fetch metadata from.
api.WaitFor(keycloak);
if (builder.ExecutionContext.IsRunMode)
{
    api.WithEnvironment("Identity__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/starterapp"))
       .WithEnvironment("Identity__RequireHttpsMetadata", "false");
}
else
{
    api.WithEnvironment("Identity__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/starterapp"))
       .WithEnvironment("Identity__MetadataAddress", ReferenceExpression.Create($"{Internal("keycloak")}/realms/starterapp/.well-known/openid-configuration"));
}

// The web app (web/) runs as the Vite dev server in run mode, with the API and identity provider
// addresses passed in as VITE_* variables; vite.config.ts proxies /api to the API so the browser
// stays on one origin. Publish mode builds web/Dockerfile instead (publish-images.yml). Keycloak
// allows no port wildcard in a redirect URI, so the dev server's port is pinned to 5173.
var web = builder.AddViteApp("web", "../../web")
       .WithEndpoint("http", endpoint => endpoint.Port = 5173)
       .WithNpm()
       .WithReference(api)
       .WithEnvironment("VITE_API_URL", api.GetEndpoint("https"))
       .WithEnvironment("VITE_OIDC_CLIENT_ID", "starterapp-web")
       .WaitFor(api);
web.WithEnvironment("VITE_OIDC_AUTHORITY", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/starterapp"))
   .WaitFor(keycloak);

// The Service Bus subscribers run in the Functions base image locally too, so they behave the same
// as the deployed worker.
var functions = builder.AddDockerfile("functions", repoRoot, "src/StarterApp.Functions/Dockerfile")
       // The Functions host serves a landing page on port 8080 once the worker is up. Exposing it lets the
       // end-to-end tests and the dashboard see that the slowest resource is really ready. The API's
       // readiness probe says nothing about this container.
       .WithHttpEndpoint(targetPort: 8080)
       .WithReference(payloadArchive)
       .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
       // The Functions image only writes the host's own lines (start-up, trigger listeners, invocations)
       // to the console when asked. Without this a deployed subscriber is a black box.
       .WithEnvironment("AzureFunctionsJobHost__Logging__Console__IsEnabled", "true")
       .WithEnvironment(context =>
       {
           ((IResourceWithAzureFunctionsConfig)serviceBus.Resource).ApplyAzureFunctionsConfiguration(context.EnvironmentVariables, "servicebus");
           ((IResourceWithAzureFunctionsConfig)storage.Resource).ApplyAzureFunctionsConfiguration(context.EnvironmentVariables, "AzureWebJobsStorage");
       })
       .WithEnvironment("ConnectionStrings__payloadarchive", payloadArchive.Resource.ConnectionStringExpression)
       // The cleanup function records its runs in the job_runs table.
       .WithEnvironment("ConnectionStrings__database", db.Resource.ConnectionStringExpression)
       .WithEnvironment("PayloadCapture__RequireArchiveStore", "true")
       .WithEnvironment("PayloadCapture__ServiceBusFailureMode", "FailClosed")
       .WithEnvironment("PayloadCapture__CleanupCron", "0 0 * * * *")
       .WithEnvironment("JobWatch__Cron", "0 */15 * * * *")
       .WaitFor(serviceBus)
       .WaitFor(payloadArchive);

// The emulator only accepts a keyed connection string, which the Functions host reads from the bare
// servicebus setting. When published, ApplyAzureFunctionsConfiguration sets
// servicebus__fullyQualifiedNamespace for the managed identity instead, and a bare servicebus
// setting holding an endpoint would override it.
if (builder.ExecutionContext.IsRunMode)
{
    functions.WithReference(serviceBus)
             .WithEnvironment("servicebus", serviceBus.Resource.ConnectionStringExpression);
}
else
{
    // When deployed, the trigger connection must come from identity settings only. The Functions host
    // reads ConnectionStrings:servicebus before servicebus__fullyQualifiedNamespace. WithReference would
    // put the endpoint URL in the first one, the host would try to parse a URL as a connection string,
    // and the listeners would never start. So the role assignment, the other thing WithReference does,
    // is requested on its own. ApplyAzureFunctionsConfiguration also puts the full endpoint URL in
    // servicebus__fullyQualifiedNamespace where the extension wants a bare hostname, so
    // serviceBusHostName is set here by hand.
    functions.WithRoleAssignments(serviceBus, ServiceBusBuiltInRole.AzureServiceBusDataOwner)
             .WithEnvironment("servicebus__fullyQualifiedNamespace", serviceBus.GetOutput("serviceBusHostName"));
}

// A dev tunnel exposes the API to the internet for webhook and mobile testing. Turn it on with
// dotnet run -- --devtunnel, or set ENABLE_DEV_TUNNEL=true.
if (args.Contains("--devtunnel") || Environment.GetEnvironmentVariable("ENABLE_DEV_TUNNEL") == "true")
{
    // The tunnelled API accepts tokens from the local Keycloak, whose realm ships well-known dev
    // credentials. Anyone who can reach the tunnel can mint a valid token. Putting that on the
    // internet, even behind a Microsoft-authenticated dev tunnel, has to be a deliberate decision, not
    // a side effect of a convenience flag.
    if (Environment.GetEnvironmentVariable("DEV_TUNNEL_ACK_DEV_IDP") != "true")
        throw new InvalidOperationException(
            "Refusing to start the dev tunnel: the API accepts tokens from the local dev Keycloak, " +
            "whose realm ships well-known development credentials, so anyone reaching the tunnel can " +
            "mint a valid token. Set DEV_TUNNEL_ACK_DEV_IDP=true to acknowledge exposing this surface " +
            "through the tunnel.");

    builder.AddDevTunnel("api-tunnel")
           .WithReference(api);
}

builder.Build().Run();
