var builder = DistributedApplication.CreateBuilder(args);

// Add Seq for centralized logging
var seq = builder.AddSeq("seq")
                 .WithLifetime(ContainerLifetime.Persistent);

// Add SQL Server with persistent lifetime and masked password for better security
var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("database");

// Add the API project with reference to the database
builder.AddProject<Projects.StarterApp_Api>("api")
       .WithReference(db)
       .WithEnvironment("SEQ_URL", seq.GetEndpoint("http"))
       .WaitFor(db)
       .WaitFor(seq);

// Add the database migrator as a separate service
builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
       .WithReference(db)
       .WithEnvironment("SEQ_URL", seq.GetEndpoint("http"))
       .WaitFor(db)
       .WaitFor(seq);

// After adding all resources, run the app...
builder.Build().Run();




