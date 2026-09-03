using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StarterApp.ServiceDefaults.Payloads;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static IHostApplicationBuilder AddPayloadCapture(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<PayloadCaptureOptions>()
            .Bind(builder.Configuration.GetSection("PayloadCapture"))
            .ValidateDataAnnotations()
            .Validate(options => !options.RequireArchiveStore || HasPayloadArchiveStore(options, builder.Configuration),
                "PayloadCapture:RequireArchiveStore is true, but no payload archive connection string or account URI is configured.")
            .ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IPayloadRedactor, JsonPayloadRedactor>();
        builder.Services.AddSingleton<IPayloadCaptureSink, PayloadCaptureSink>();
        builder.Services.AddSingleton<IArtifactCaptureSink, ArtifactCaptureSink>();
        // One BlobServiceClient per process, resolved from the bound options. The archive store and
        // the API's payload-archive health check both take it through the provider, so a health
        // probe reuses the warm client and its credential's token cache instead of re-walking the
        // DefaultAzureCredential chain on every call.
        builder.Services.AddSingleton(provider => new PayloadArchiveClientProvider(
            PayloadArchiveConfiguration.CreateClient(
                provider.GetRequiredService<IOptions<PayloadCaptureOptions>>().Value,
                builder.Configuration)));

        builder.Services.AddSingleton<IPayloadArchiveStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PayloadCaptureOptions>>();
            var client = provider.GetRequiredService<PayloadArchiveClientProvider>().Client;
            return client is not null
                ? new AzureBlobPayloadArchiveStore(client, options)
                : new NullPayloadArchiveStore();
        });

        return builder;
    }

    private static bool HasPayloadArchiveStore(PayloadCaptureOptions options, IConfiguration configuration)
    {
        return PayloadArchiveConfiguration.IsConfigured(options, configuration);
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks("/health");

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}


