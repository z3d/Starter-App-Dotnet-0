var builder = DistributedApplication.CreateBuilder(args);

// Add Seq for centralized logging
var seq = builder.AddSeq("seq")
                 .WithLifetime(ContainerLifetime.Persistent);

// Add SQL Server with persistent lifetime and masked password for better security
var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("database");

// Add the API project with reference to the database
var api = builder.AddProject<Projects.StarterApp_Api>("api")
       .WithReference(db)
       .WithEnvironment("SEQ_URL", seq.GetEndpoint("http"))
       .WaitFor(db)
       .WaitFor(seq);

// Dev Tunnel: expose the API to the internet for webhook/mobile testing
// Enable with: dotnet run -- --devtunnel  OR  set ENABLE_DEV_TUNNEL=true
if (args.Contains("--devtunnel") || Environment.GetEnvironmentVariable("ENABLE_DEV_TUNNEL") == "true")
{
    builder.AddDevTunnel("api-tunnel")
           .WithReference(api);
}

// Add the database migrator as a separate service
builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
       .WithReference(db)
       .WithEnvironment("SEQ_URL", seq.GetEndpoint("http"))
       .WaitFor(db)
       .WaitFor(seq);

// After adding all resources, run the app...
builder.Build().Run();




