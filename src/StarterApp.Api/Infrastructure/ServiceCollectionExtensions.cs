using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StarterApp.Api.Infrastructure.HealthChecks;
using StarterApp.Api.Infrastructure.Outbox;
using StarterApp.ServiceDefaults;

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

                JwtIdentityOpenApi.ApplySecuritySchemes(document);
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        // A handler that opens its own transaction must run it inside CreateExecutionStrategy().ExecuteAsync or the first transient fault throws.
        services.TryAddScoped<OwnerPolicyEvaluationTracker>();
        services.AddScoped<OwnerAuthorizationWriteGuard>();
        services.TryAddSingleton(TimeProvider.System);

        // One data source per process so the managed-identity password provider is configured once.
        services.AddDatabaseDataSource(connectionString);
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), postgres =>
                postgres.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorCodesToAdd: null))
                   .EnableSensitiveDataLogging(false)
                   .AddInterceptors(new DomainEventsInterceptor(provider.GetRequiredService<TimeProvider>()), provider.GetRequiredService<OwnerAuthorizationWriteGuard>()));

        // Transient, not scoped: Npgsql cannot run two queries on one connection at once.
        services.AddTransient<System.Data.IDbConnection>(provider =>
            provider.GetRequiredService<NpgsqlDataSource>().CreateConnection());

        return services;
    }

    internal static readonly string[] ExposedResponseHeaders = ["WWW-Authenticate", "X-Correlation-ID", "Retry-After"];

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
                          // Omitting X-Correlation-ID here blocks browser callers from supplying their own.
                          .WithHeaders("Authorization", "Content-Type", "X-Correlation-ID");

                // Browsers hide non-safelisted response headers from cross-origin scripts unless exposed.
                policy.WithExposedHeaders(ExposedResponseHeaders);
            });
        });

        return services;
    }

    public static IServiceCollection AddJwtIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<JwtIdentityOptions>()
            .Bind(configuration.GetSection(JwtIdentityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => IsDevelopmentLike(environment) || !string.IsNullOrWhiteSpace(options.Authority),
                "Identity:Authority is required outside Development or Testing environments.")
            .Validate(options => IsDevelopmentLike(environment) || options.RequireHttpsMetadata,
                "Identity:RequireHttpsMetadata=false is only allowed in Development or Testing environments.")
            .Validate(AuthorityIsWellFormed,
                "Identity:Authority must be an absolute http(s) URI, and https unless Identity:RequireHttpsMetadata is false.")
            .ValidateOnStart();

        // Self-contained JWTs only; never add a token-introspection call to this path.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<Microsoft.Extensions.Options.IOptions<JwtIdentityOptions>>((bearer, identityOptions) =>
            {
                var identity = identityOptions.Value;
                if (!string.IsNullOrWhiteSpace(identity.Authority))
                    bearer.Authority = identity.Authority;
                if (!string.IsNullOrWhiteSpace(identity.MetadataAddress))
                {
                    bearer.MetadataAddress = identity.MetadataAddress;
                    // The document fetched from the internal name still states the public issuer.
                    bearer.TokenValidationParameters.ValidIssuer = identity.Authority;
                }
                bearer.RequireHttpsMetadata = identity.RequireHttpsMetadata;
                // Inbound claim remapping would rename sub/tid/scope/amr out from under JwtIdentityMiddleware.
                bearer.MapInboundClaims = false;
                // Keeps GetTokenAsync("access_token") from handing the raw token back to app code.
                bearer.SaveToken = false;
                bearer.TokenValidationParameters.ValidAlgorithms = JwtIdentityOptions.AllowedSigningAlgorithms;
                bearer.TokenValidationParameters.ValidateAudience = true;
                bearer.TokenValidationParameters.ValidAudience = identity.Audience;
                bearer.TokenValidationParameters.ValidateIssuer = true;
                bearer.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(identity.ClockSkewSeconds);
                bearer.TokenValidationParameters.NameClaimType = "sub";
            });

        // Catches a route mapped outside /api/v1, which the convention tests do not see.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.TryAddScoped<OwnerPolicyEvaluationTracker>();
        services.AddScoped<IOwnerOnlyPolicy, OwnerOnlyPolicy>();

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
            options.OnRejected = (rejection, _) =>
            {
                if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    rejection.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    // Partitioned by verified identity so one tenant cannot starve another; client IP only when anonymous.
    internal static string ResolveRateLimitPartitionKey(HttpContext httpContext)
    {
        var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();
        return currentUser is { IsAuthenticated: true }
            ? $"identity:{currentUser.TenantId}:{currentUser.Subject}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    // A malformed authority otherwise surfaces only when the bearer handler builds its metadata address.
    internal static bool AuthorityIsWellFormed(JwtIdentityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Authority))
            return true;

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority))
            return false;

        if (authority.Scheme == Uri.UriSchemeHttps)
            return true;

        return authority.Scheme == Uri.UriSchemeHttp && !options.RequireHttpsMetadata;
    }

    private static bool IsDevelopmentLike(IHostEnvironment environment)
    {
        return environment.IsDevelopment() ||
            environment.EnvironmentName == "Testing";
    }

    // "durable" is the set /healthiness runs; Service Bus and the archive register only when configured.
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready", "durable"]);

        // The in-memory fallback is per-process, so tagging it durable would mask a missing Redis.
        if (!string.IsNullOrEmpty(configuration.GetConnectionString("redis")))
            healthChecks.AddCheck<DistributedCacheHealthCheck>("distributed-cache", tags: ["durable"]);

        if (!string.IsNullOrEmpty(configuration.GetConnectionString("servicebus")))
            healthChecks.AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["durable"]);

        if (StarterApp.ServiceDefaults.Payloads.PayloadArchiveConfiguration.IsConfigured(configuration))
        {
            // AddCheck<T> alone activates a fresh instance per run; registering it makes HealthCheckService reuse one.
            services.AddSingleton<PayloadArchiveHealthCheck>();
            healthChecks.AddCheck<PayloadArchiveHealthCheck>("payload-archive", tags: ["durable"]);
        }

        return services;
    }

    public static IServiceCollection AddServiceBusPublisher(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("servicebus");
        if (string.IsNullOrEmpty(connectionString))
        {
            // Outside dev a missing connection string would boot green and accumulate outbox rows forever.
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

        services.AddSingleton(_ => AzureClientAuthentication.CreateServiceBusClient(connectionString));
        services.AddSingleton(provider =>
            provider.GetRequiredService<ServiceBusClient>().CreateSender(options.TopicName));
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
