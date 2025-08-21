using StarterApp.Api.Data;
using StarterApp.Api.Application.Commands;
using StarterApp.Api.Application.Queries;
using StarterApp.Api.Infrastructure.Repositories;
using StarterApp.Domain.Interfaces;
using System.Reflection;
using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults
builder.AddServiceDefaults();

// Configure Docker environment settings if needed
if (builder.Environment.EnvironmentName == "Docker")
    builder.Configuration.AddJsonFile("appsettings.Docker.json", optional: false);

// Configure Serilog using settings from appsettings.json
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
);

// Add services to the container
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

// Add native OpenAPI support with .NET 9
builder.Services.AddOpenApi(options => {
    options.AddDocumentTransformer((document, context, cancellationToken) => {
        document.Info.Title = "Starter App API";
        document.Info.Version = "v1";
        document.Info.Description = "A sample API for the Starter App";
        document.Info.Contact = new() {
            Name = "Starter App Team"
        };
        return Task.CompletedTask;
    });
});

// Add Database context
// Try Aspire-provided connection string first (new simplified name), then fall back to others
var databaseConnection = builder.Configuration.GetConnectionString("database");
var dockerLearningConnection = builder.Configuration.GetConnectionString("DockerLearning");
var sqlserverConnection = builder.Configuration.GetConnectionString("sqlserver");
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");

// Prioritize the new simplified "database" connection string from Aspire
var connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection;

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("No connection string found. Please check your configuration.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register IDbConnection for Dapper queries
builder.Services.AddScoped<System.Data.IDbConnection>(provider =>
    new Microsoft.Data.SqlClient.SqlConnection(connectionString));

// Register services for CQRS pattern
builder.Services.AddScoped<IProductCommandService, ProductCommandService>();
builder.Services.AddScoped<IProductQueryService, ProductQueryService>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICustomerCommandService, CustomerCommandService>();
builder.Services.AddScoped<ICustomerQueryService, CustomerQueryService>();
builder.Services.AddScoped<IOrderCommandService, OrderCommandService>();
builder.Services.AddScoped<IOrderQueryService, OrderQueryService>();
builder.Services.AddMediatR(Assembly.GetExecutingAssembly());
builder.Services.AddControllers();

// Add CORS (restrict in production)
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                  .WithMethods("GET", "POST", "PUT", "DELETE")
                  .WithHeaders("Authorization", "Content-Type");
    });
});

// Add Health checks and rate limiting
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options => {
    options.AddFixedWindowLimiter("fixed", options => {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 5;
    });
    
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

try
{
    Log.Information("Starting up application");
    
    // Debug logging - only in development environment for security
    if (app.Environment.IsDevelopment())
    {
        Log.Information("=== CONNECTION STRING DEBUG ===");
        Log.Information("database connection (primary): {DatabaseConnection}", databaseConnection);
        Log.Information("DockerLearning connection: {DockerLearningConnection}", dockerLearningConnection);
        Log.Information("sqlserver connection: {SqlServerConnection}", sqlserverConnection);
        Log.Information("DefaultConnection: {DefaultConnection}", defaultConnection);

        // Check all connection strings
        var allConnectionStrings = builder.Configuration.GetSection("ConnectionStrings").GetChildren();
        Log.Information("All available connection strings:");
        foreach (var conn in allConnectionStrings)
        {
            Log.Information("  {Key}: {Value}", conn.Key, conn.Value);
        }

        // Also check environment variables
        Log.Information("Relevant environment variables:");
        foreach (var envVar in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(e => e.Key?.ToString()?.Contains("Connection", StringComparison.OrdinalIgnoreCase) == true || 
                        e.Key?.ToString()?.Contains("DockerLearning", StringComparison.OrdinalIgnoreCase) == true ||
                        e.Key?.ToString()?.Contains("SQL", StringComparison.OrdinalIgnoreCase) == true))
        {
            Log.Information("  {Key}: {Value}", envVar.Key, envVar.Value);
        }

        Log.Information("Using connection string: {ConnectionString}", connectionString);
        Log.Information("=== END CONNECTION STRING DEBUG ===");
    }
    else
    {
        // Production - only log that connection was found (no sensitive details)
        Log.Information("Database connection configured: {HasConnection}", !string.IsNullOrEmpty(connectionString));
    }

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Docker")
    {
        // Use the new native .NET 9 OpenAPI endpoints
        app.MapOpenApi(); // Serves the OpenAPI document at /openapi/v1.json
        
        // Configure SwaggerUI to use the new OpenAPI endpoint
        app.UseSwaggerUI(options => {
            options.SwaggerEndpoint("/openapi/v1.json", "Docker Learning API v1");
            options.RoutePrefix = "swagger";
        });
    }
    else
    {
        app.UseHsts();
    }

    // Use .NET 9 Problem Details with StatusCodeSelector for centralized exception handling
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        StatusCodeSelector = ex => ex switch
        {
            ArgumentNullException => StatusCodes.Status400BadRequest,
            ArgumentOutOfRangeException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        }
    });
    
    app.UseStatusCodePages();

    // Security headers middleware
    app.Use(async (context, next) => {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        if (!app.Environment.IsDevelopment())
            context.Response.Headers.Append("Content-Security-Policy", 
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'");

        await next();    });

    // Use DbUp for database migrations
    if (connectionString != null)
    {
        Log.Information("Starting database migrations...");
        var migrationResult = DatabaseMigrator.MigrateDatabase(connectionString);
        if (migrationResult)
        {
            Log.Information("Database migrations completed successfully.");
        }
        else
        {
            Log.Error("Database migrations failed!");
            if (!app.Environment.IsDevelopment())
                Environment.Exit(1);
        }
    }
    else
    {
        Log.Warning("Connection string 'DockerLearning' or 'DefaultConnection' is missing or null. Skipping database migration.");
    }

    // Enable middlewares
    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();
    app.UseRouting();
    app.MapControllers();
    app.MapHealthChecks("/health");

    // Map Aspire service defaults endpoints
    app.MapDefaultEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed: {ExMessage}", ex.Message);
    Environment.Exit(1);
}
finally
{
    Log.CloseAndFlush();
}
