var builder = DistributedApplication.CreateBuilder(args);

// Add Seq for centralized logging
var seq = builder.AddSeq("seq")
                 .WithLifetime(ContainerLifetime.Persistent);

// Add SQL Server with persistent lifetime and masked password for better security
var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("database");

// Add Redis for distributed caching
var redis = builder.AddRedis("redis")
                   .WithLifetime(ContainerLifetime.Persistent);

// Add Azure Service Bus emulator for domain event messaging
var serviceBus = builder.AddAzureServiceBus("servicebus")
                        .RunAsEmulator(emulator => emulator.WithConfigurationFile("../../config/servicebus-emulator.json"));

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




