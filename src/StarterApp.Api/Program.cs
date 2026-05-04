using Scalar.AspNetCore;
using Serilog.Enrichers.Sensitive;
using StarterApp.Api.Endpoints;
using StarterApp.Api.Infrastructure.Payloads;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

if (builder.Environment.EnvironmentName == "Docker")
    builder.Configuration.AddJsonFile("appsettings.Docker.json", optional: false);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithSensitiveDataMasking(_ => { });

    var seqUrl = context.Configuration["SEQ_URL"] ?? context.Configuration["SeqUrl"];
    if (!string.IsNullOrEmpty(seqUrl))
        configuration.WriteTo.Seq(seqUrl);
});

var connectionString = builder.Configuration.GetConnectionString("database")
    ?? throw new InvalidOperationException("Connection string 'database' not found. Ensure Aspire is configured correctly.");

if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis");
else
    builder.Services.AddDistributedMemoryCache();

builder.Services.AddApiProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApiOpenApi();
builder.Services.AddPersistence(connectionString);
builder.Services.AddMediator(Assembly.GetExecutingAssembly());
builder.Services.AddApiCors(builder.Configuration, builder.Environment);
builder.Services.AddApiRateLimiting();
builder.Services.AddApiHealthChecks();
builder.Services.AddServiceBusPublisher(builder.Configuration);
builder.AddPayloadCapture();

var app = builder.Build();

try
{
    Log.Information("Starting up application");

    if (app.Environment.IsDevelopment())
        Log.Information("Database connection configured: {ConnectionString}", MaskConnectionStringPassword(connectionString));
    else
        Log.Information("Database connection configured successfully");

    // Middleware pipeline — order matters
    if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Docker")
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }
    else
    {
        app.UseHsts();
    }

    app.UsePayloadCapture();
    app.UseExceptionHandling();
    app.UseSecurityHeaders();
    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();
    app.UseRouting();

    app.MapApiEndpoints();
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    });
    app.MapHealthChecks("/alive", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    });

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

static string? MaskConnectionStringPassword(string? connectionString)
{
    if (string.IsNullOrEmpty(connectionString))
        return connectionString;

    return System.Text.RegularExpressions.Regex.Replace(
        connectionString,
        @"(password|pwd)\s*=\s*[^;]+",
        "$1=***MASKED***",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
