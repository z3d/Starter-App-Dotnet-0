var builder = DistributedApplication.CreateBuilder(args);

// Add SQL Server with persistent lifetime for better reliability
var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("database");

// Add the API project with reference to the database
builder.AddProject<Projects.DockerLearningApi>("api")
       .WithReference(db)
       .WaitFor(db);

// Add the database migrator as a separate service
builder.AddProject<Projects.DockerLearning_DbMigrator>("migrator")
       .WithReference(db)
       .WaitFor(db);

// After adding all resources, run the app...
builder.Build().Run();
