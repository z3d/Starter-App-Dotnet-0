using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StarterApp.Api.Infrastructure.HealthChecks;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var exception = context.HttpContext.Features
                    .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()
                    ?.Error;

                if (exception is not ValidationException validationException)
                    return;

                context.ProblemDetails.Extensions["errors"] = validationException.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).ToArray(),
                        StringComparer.Ordinal);
            };
        });

        return services;
    }

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = "Starter App API";
                document.Info.Version = "v1";
                document.Info.Description = "A sample API for the Starter App built with .NET 10 Minimal APIs";
                document.Info.Contact = new() { Name = "Starter App Team" };

                // Scalar renders an Auth panel only for declared security schemes; the identity
                // layer owns the header names (convention-enforced), so it declares them.
                GatewayIdentityOpenApi.ApplySecuritySchemes(document);
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        // EnableRetryOnFailure is safe here because:
        //   1. ApplicationDbContext uses a single-SaveChanges outbox (no user transaction),
        //      so the retrying execution strategy does not have to refuse the flow.
        //   2. CreateOrderCommandHandler's user transaction (stock reservation + order save)
        //      is wrapped in Database.CreateExecutionStrategy().ExecuteAsync, which is the
        //      retry-safe pattern documented by Microsoft.
        // If a future handler opens BeginTransaction without wrapping in an execution strategy,
        // the first transient fault will throw at runtime with a clear message.
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, postgres =>
                postgres.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorCodesToAdd: null))
                   .EnableSensitiveDataLogging(false));

        // Dapper reads use this connection. Query handlers wrap their calls in
        // PostgresRetryPolicy.ExecuteAsync so read-side transient faults get the same retry posture
        // as EF Core writes.
        // Transient (not scoped): each injecting query handler gets its own NpgsqlConnection, so a
        // future Task.WhenAll over two query handlers in one request can't collide on a single
        // connection (Npgsql has no MARS). Queries are read-only with no shared transaction, and
        // connection pooling reuses the physical sockets, so per-resolution connections are cheap.
        services.AddTransient<System.Data.IDbConnection>(provider =>
            new Npgsql.NpgsqlConnection(connectionString));

        return services;
    }

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (environment.IsDevelopment())
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                else
                    policy.WithOrigins(configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                          .WithMethods("GET", "POST", "PUT", "DELETE")
                          .WithHeaders("Authorization", "Content-Type");
            });
        });

        return services;
    }

    public static IServiceCollection AddGatewayIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<GatewayIdentityOptions>()
            .Bind(configuration.GetSection(GatewayIdentityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => IsDevelopmentLike(environment) || options.Mode == GatewayIdentityMode.Required,
                "GatewayIdentity:Mode=UnsignedDevelopment is only allowed in Development or Testing environments.")
            .Validate(options => options.Mode != GatewayIdentityMode.Required || !string.IsNullOrWhiteSpace(options.SigningKey),
                "GatewayIdentity:SigningKey is required when GatewayIdentity:Mode=Required.")
            .Validate(options => options.Mode != GatewayIdentityMode.Required ||
                                 (!string.IsNullOrWhiteSpace(options.SigningKey) && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32),
                "GatewayIdentity:SigningKey must be at least 32 bytes when GatewayIdentity:Mode=Required.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.AddScoped<OwnerPolicyEvaluationTracker>();
        services.AddScoped<IOwnerOnlyPolicy, OwnerOnlyPolicy>();
        services.AddSingleton<IGatewayAssertionValidator, GatewayAssertionValidator>();

        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var key = ResolveRateLimitPartitionKey(httpContext);
                var limits = httpContext.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitingOptions>>().Value;

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit,
                    Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = limits.QueueLimit
                });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    // Authenticated traffic is partitioned by the verified gateway identity so one tenant
    // cannot starve another; only unauthenticated traffic falls back to client IP.
    internal static string ResolveRateLimitPartitionKey(HttpContext httpContext)
    {
        var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();
        return currentUser is { IsAuthenticated: true }
            ? $"identity:{currentUser.TenantId}:{currentUser.Subject}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static bool IsDevelopmentLike(IHostEnvironment environment)
    {
        return environment.IsDevelopment() ||
            environment.EnvironmentName == "Testing";
    }

    // The "durable" tag marks checks against deployable backing resources; /healthiness runs
    // exactly that set. Service Bus and the payload archive register conditionally, mirroring
    // their service registrations, so standalone dev/tests without them stay healthy.
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready", "durable"])
            .AddCheck<DistributedCacheHealthCheck>("distributed-cache", tags: ["durable"]);

        if (!string.IsNullOrEmpty(configuration.GetConnectionString("servicebus")))
            healthChecks.AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["durable"]);

        if (HasPayloadArchiveConfiguration(configuration))
            healthChecks.AddCheck<PayloadArchiveHealthCheck>("payload-archive", tags: ["durable"]);

        return services;
    }

    private static bool HasPayloadArchiveConfiguration(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration["PayloadCapture:ConnectionString"]) ||
            !string.IsNullOrWhiteSpace(configuration["PayloadCapture:AccountUri"]) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("payloadarchive")) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("payloadstorage"));
    }

    public static IServiceCollection AddServiceBusPublisher(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("servicebus");
        if (string.IsNullOrEmpty(connectionString))
        {
            // The no-op fallback exists for tests and standalone dev only. In production-like
            // environments a missing/typo'd connection string would otherwise boot green, pass
            // /health/ready (database-only), and silently accumulate outbox rows forever — so
            // fail startup loudly, mirroring the GatewayIdentity:Mode environment gate.
            if (!IsDevelopmentLike(environment))
                throw new InvalidOperationException(
                    "ConnectionStrings:servicebus is required outside Development/Testing environments. " +
                    "Domain events would silently stop publishing; configure the Service Bus connection string.");

            return services;
        }

        services.AddOptions<OutboxProcessorOptions>()
            .Bind(configuration.GetSection("OutboxProcessor"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new OutboxProcessorOptions();
        configuration.GetSection("OutboxProcessor").Bind(options);

        services.AddSingleton(_ => new ServiceBusClient(connectionString));
        services.AddSingleton(provider =>
            provider.GetRequiredService<ServiceBusClient>().CreateSender(options.TopicName));
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
