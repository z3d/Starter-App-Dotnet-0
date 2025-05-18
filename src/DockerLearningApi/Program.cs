using DockerLearningApi.Data;
using DockerLearning.Domain.Interfaces;
using DockerLearningApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Reflection;
using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

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

// Add CORS (restrict in production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // In production, lock down CORS to specific origins
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                  .WithMethods("GET", "POST", "PUT", "DELETE")
                  .WithHeaders("Authorization", "Content-Type");
        }
    });
});

// Add Health checks
builder.Services.AddHealthChecks();

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", options =>
    {
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

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    else
    {
        // Add HSTS in production
        app.UseHsts();
    }

    // Global exception handler
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            var exception = exceptionHandlerPathFeature?.Error;

            if (exception != null)
            {
                // Log the full exception details for debugging
                Log.Error(exception, "Unhandled exception: {ExMessage}", exception.Message);
            }

            // In development, provide more details about the error
            if (app.Environment.IsDevelopment())
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    error = exception?.Message ?? "An unexpected error occurred",
                    stackTrace = exception?.StackTrace
                });
            }
            else
            {
                // In production, provide a generic message
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "An unexpected error occurred. Please try again later."
                });
            }
        });
    });

    // Security headers middleware
    app.Use(async (context, next) =>
    {
        // Add security headers
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // Add CSP in non-development environments
        if (!app.Environment.IsDevelopment())
        {
            context.Response.Headers.Append(
                "Content-Security-Policy",
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'");
        }

        await next();
    });

    // Use DbUp for database migrations
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (connectionString != null)
    {
        if (!DatabaseMigrator.MigrateDatabase(connectionString))
        {
            // If migrations fail, we might want to stop the application from fully starting
            if (!app.Environment.IsDevelopment())
            {
                return 1;
            }
        }
    }
    else
    {
        Log.Warning("Connection string 'DefaultConnection' is missing or null. Skipping database migration.");
    }

    // Enable middlewares
    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();
    app.UseRouting();

    // Add authorization if needed
    // app.UseAuthentication();
    // app.UseAuthorization();

    app.MapControllers();

    // Health check endpoint
    app.MapHealthChecks("/health");

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed: {ExMessage}", ex.Message);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
