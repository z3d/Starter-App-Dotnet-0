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

    if (app.Environment.IsDevelopment())
        Log.Information("Database connection configured: {ConnectionString}", StarterApp.ServiceDefaults.ConnectionStringDescriptor.Describe(connectionString));
    else
        Log.Information("Database connection configured successfully");

    // Middleware pipeline — order matters
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();

        // Config probe for the dev-only walkthrough (wwwroot/demo.html, served further down the
        // pipeline). Serving is dev-gated here, and Release builds exclude wwwroot/** from output
        // entirely (see the Content Remove in the csproj) — the page embeds well-known dev-realm
        // credentials, so it must not ride along in production images as a dead file.
        app.MapGet("/demo/config", (Microsoft.Extensions.Options.IOptions<StarterApp.Api.Infrastructure.Identity.JwtIdentityOptions> identity) =>
            Results.Ok(new { authority = identity.Value.Authority })).AllowAnonymous();
    }
    else
    {
        // The framework middleware keeps AddHsts as the extension point. Its header is written
        // eagerly and is lost on exception-mapped responses; that is accepted — behind the TLS
        // terminator Request.IsHttps is false anyway, and a browser already pinned by a success
        // response is not un-pinned by one error response without the header.
        app.UseHsts();
    }

    app.UseMiddleware<PayloadCaptureMiddleware>();
    app.UseExceptionHandling();
    app.UseSecurityHeaders();
    app.UseHttpsRedirection();

    // Dev-only static hosting for the walkthrough page. Deliberately behind payload capture
    // (the capture-first recorded decision admits no exceptions), the security headers, and
    // the https redirect, so wwwroot responses are audited, hardened, and never served over
    // plain http.
    if (app.Environment.IsDevelopment())
        app.UseStaticFiles();

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
    // A returned exit code lets the finally block flush the batching sinks; Environment.Exit
    // would terminate before it ran and lose the one line explaining the crash.
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
