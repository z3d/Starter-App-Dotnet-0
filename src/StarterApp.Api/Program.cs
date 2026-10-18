using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using StarterApp.Api.Endpoints;
using StarterApp.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseSerilog((context, services, configuration) =>
    SerilogConfiguration.Apply(configuration, context.Configuration, services));

var connectionString = builder.Configuration.GetConnectionString("database")
    ?? throw new InvalidOperationException("Connection string 'database' not found. Ensure Aspire is configured correctly.");

if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis", configureOptions: options => AzureClientAuthentication.ConfigureRedis(options));
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
builder.Services.AddJwtIdentity(builder.Configuration, builder.Environment);
builder.Services.AddApiRateLimiting();
builder.Services.AddApiHealthChecks(builder.Configuration);
builder.Services.AddServiceBusPublisher(builder.Configuration, builder.Environment);
builder.AddPayloadCapture();
builder.AddJobRunRecording();

var app = builder.Build();

try
{
    Log.Information("Starting up application");

    // Logged at start-up. It is the first thing to check when the app cannot reach its database.
    Log.Information("Database authentication: {DatabaseAuthentication}", StarterApp.ServiceDefaults.DatabaseAuthentication.Describe(connectionString));
    if (app.Environment.IsDevelopment())
        Log.Information("Database connection configured: {ConnectionString}", StarterApp.ServiceDefaults.ConnectionStringDescriptor.Describe(connectionString));

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();
    }
    else
    {
        // Error responses lose the HSTS header. That is accepted: a browser that has already seen it stays pinned.
        app.UseHsts();
    }

    app.UseMiddleware<PayloadCaptureMiddleware>();
    app.UseExceptionHandling();
    app.UseSecurityHeaders();
    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRouting();
    app.UseJwtIdentity();
    app.UseRateLimiter();

    app.MapApiEndpoints();

    app.MapProbeEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed: {ExMessage}", ex.Message);
    // Return an exit code rather than calling Environment.Exit, so the finally block can flush the log sinks. Otherwise the line that explains the crash is lost.
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
