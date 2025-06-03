var builder = DistributedApplication.CreateBuilder(args);

// Add SQL Server with specific password and persistent lifetime for better reliability
var password = builder.AddParameter("sql-password", value: "Your_password123", secret: true);
var sqlServer = builder.AddSqlServer("sqlserver", password)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent); // SQL Server is slow to start, use persistent

var database = sqlServer.AddDatabase("DockerLearning");

// Add the API project with reference to the database
var api = builder.AddProject<Projects.DockerLearningApi>("api")
    .WithReference(database)
    .WaitFor(database);

// Add the database migrator as a separate service
var migrator = builder.AddProject<Projects.DockerLearning_DbMigrator>("migrator")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
