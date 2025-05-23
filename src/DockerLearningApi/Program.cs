using DockerLearningApi.Data;
using System.Reflection;
using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

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

// Add services to the container.
// Replace AddOpenApi with proper Swagger configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { 
        Title = "Docker Learning API", 
        Version = "v1",
        Description = "A sample API for Docker learning",
        Contact = new OpenApiContact
        {
            Name = "Docker Learning Team"
        }
    });
});

// Add Database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register services for CQRS pattern
// Register interfaces and implementations
builder.Services.AddScoped<IProductCommandService, ProductCommandService>();
builder.Services.AddScoped<IProductQueryService, ProductQueryService>();

// Keeping repository for backward compatibility during transition
// This can be removed once all code is migrated to the query/command services
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
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                  .WithMethods("GET", "POST", "PUT", "DELETE")
                  .WithHeaders("Authorization", "Content-Type");
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
    if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Docker")
    {
        // Enable Swagger in both Development and Docker environments
        app.UseSwagger();
        app.UseSwaggerUI(c => {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Docker Learning API v1");
            c.RoutePrefix = "swagger";
        });
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
                // Log the full exception details for debugging
                Log.Error(exception, "Unhandled exception: {ExMessage}", exception.Message);

            // In development, provide more details about the error
            if (app.Environment.IsDevelopment())
                await context.Response.WriteAsJsonAsync(new { error = exception?.Message ?? "An unexpected error occurred", stackTrace = exception?.StackTrace });
            else
                // In production, provide a generic message
                await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred. Please try again later." });
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
            context.Response.Headers.Append("Content-Security-Policy", 
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'");

        await next();
    });

    // Use DbUp for database migrations
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (connectionString != null)
    {
        if (!DatabaseMigrator.MigrateDatabase(connectionString) && !app.Environment.IsDevelopment())
            Environment.Exit(1);
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
    app.MapControllers();
    app.MapHealthChecks("/health");

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
