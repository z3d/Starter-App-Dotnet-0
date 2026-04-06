using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

// Add Seq for centralized logging
var seq = builder.AddSeq("seq")
                 .WithLifetime(ContainerLifetime.Persistent);

// Add SQL Server with persistent lifetime
var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("database");

// Add Redis for distributed caching
var redis = builder.AddRedis("redis")
                   .WithLifetime(ContainerLifetime.Persistent);

// Add Azure Service Bus emulator for domain event messaging
// Topology defined via fluent API so Aspire serializes correlation filters correctly
var serviceBus = builder.AddAzureServiceBus("servicebus");

var domainEventsTopic = serviceBus.AddServiceBusTopic("domain-events");

domainEventsTopic.AddServiceBusSubscription("email-notifications")
    .WithProperties(sub =>
    {
        sub.DefaultMessageTimeToLive = TimeSpan.FromHours(1);
        sub.LockDuration = TimeSpan.FromSeconds(30);
        sub.MaxDeliveryCount = 5;
        sub.Rules.Add(new AzureServiceBusRule("OrderCreatedFilter")
        {
            FilterType = AzureServiceBusFilterType.CorrelationFilter,
            CorrelationFilter = new AzureServiceBusCorrelationFilter
            {
                Properties = { ["EventType"] = "order.created.v1" }
            }
        });
        sub.Rules.Add(new AzureServiceBusRule("OrderStatusChangedFilter")
        {
            FilterType = AzureServiceBusFilterType.CorrelationFilter,
            CorrelationFilter = new AzureServiceBusCorrelationFilter
            {
                Properties = { ["EventType"] = "order.status-changed.v1" }
            }
        });
    });

domainEventsTopic.AddServiceBusSubscription("inventory-reservation")
    .WithProperties(sub =>
    {
        sub.DefaultMessageTimeToLive = TimeSpan.FromHours(1);
        sub.LockDuration = TimeSpan.FromSeconds(30);
        sub.MaxDeliveryCount = 5;
        sub.Rules.Add(new AzureServiceBusRule("OrderCreatedFilter")
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
       .WithReference(serviceBus)
       .WithEnvironment("SEQ_URL", seq.GetEndpoint("http"))
       .WaitFor(db)
       .WaitFor(redis)
       .WaitFor(seq)
       .WaitFor(serviceBus)
       .WaitForCompletion(migrator);

// Add Azure Functions project for Service Bus subscribers
builder.AddProject<Projects.StarterApp_Functions>("functions")
       .WithReference(serviceBus)
       .WaitFor(serviceBus);

// Dev Tunnel: expose the API to the internet for webhook/mobile testing
// Enable with: dotnet run -- --devtunnel  OR  set ENABLE_DEV_TUNNEL=true
if (args.Contains("--devtunnel") || Environment.GetEnvironmentVariable("ENABLE_DEV_TUNNEL") == "true")
{
    builder.AddDevTunnel("api-tunnel")
           .WithReference(api);
}

// After adding all resources, run the app...
builder.Build().Run();




