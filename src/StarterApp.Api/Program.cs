using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using StarterApp.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseSerilog((context, services, configuration) =>
    SerilogConfiguration.Apply(configuration, context.Configuration, services));

var connectionString = builder.Configuration.GetConnectionString("database")
    ?? throw new InvalidOperationException("Connection string 'database' not found. Ensure Aspire is configured correctly.");

if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis");
else
    builder.Services.AddDistributedMemoryCache();

builder.Services.AddApiProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApiOpenApi();
builder.Services.AddPersistence(connectionString);
builder.Services.AddMediator(Assembly.GetExecutingAssembly());
builder.Services.AddApiCors(builder.Configuration, builder.Environment);
builder.Services.AddGatewayIdentity(builder.Configuration, builder.Environment);
builder.Services.AddApiRateLimiting();
builder.Services.AddApiHealthChecks(builder.Configuration);
builder.Services.AddServiceBusPublisher(builder.Configuration, builder.Environment);
builder.AddPayloadCapture();
builder.AddJobRunRecording();

var app = builder.Build();

try
{
    Log.Information("Starting up application");

    if (app.Environment.IsDevelopment())
        Log.Information("Database connection configured: {ConnectionString}", MaskConnectionStringPassword(connectionString));
    else
        Log.Information("Database connection configured successfully");

    // Middleware pipeline — order matters
    if (app.Environment.IsDevelopment())
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
    app.UseRouting();
    app.UseGatewayIdentity();
    app.UseRateLimiter();

    app.MapApiEndpoints();

    // Health/probe endpoints opt out of the global rate limiter. They are unauthenticated, so the
    // limiter buckets them by client IP — under k8s the kubelet probes from the node IP and would
    // share one partition with other node-egress traffic, so a 429 on /health/ready or /health/live
    // could evict or restart an otherwise-healthy pod (a self-inflicted availability flap).
    app.MapHealthChecks("/health").DisableRateLimiting();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    }).DisableRateLimiting();
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    }).DisableRateLimiting();
    app.MapHealthChecks("/alive", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    }).DisableRateLimiting();
    app.MapProbeEndpoints();

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
