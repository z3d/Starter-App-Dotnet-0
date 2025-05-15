using DockerLearningApi.Data;
using DockerLearningApi.Domain.Interfaces;
using DockerLearningApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Configure Docker environment settings if needed
if (builder.Environment.EnvironmentName == "Docker")
{
    builder.Configuration.AddJsonFile("appsettings.Docker.json", optional: false);
}

// Configure Serilog using settings from appsettings.json
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration) // Read from appsettings.json
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
);

// Add services to the container.
builder.Services.AddOpenApi();

// Add Database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add repositories
builder.Services.AddScoped<IProductRepository, ProductRepository>();

// Add MediatR - updated for MediatR 11.1.0
builder.Services.AddMediatR(Assembly.GetExecutingAssembly());

// Add controller support
builder.Services.AddControllers();

var app = builder.Build();

try
{
    Log.Information("Starting up application");

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // Use DbUp for database migrations
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (!DatabaseMigrator.MigrateDatabase(connectionString))
    {
        // If migrations fail, we might want to stop the application from fully starting
        if (!app.Environment.IsDevelopment())
        {
            return 1;
        }
    }

    app.UseHttpsRedirection();
    app.MapControllers();

    // Health check endpoint
    app.MapGet("/health", () => "Healthy");

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
