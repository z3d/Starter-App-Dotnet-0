using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
                // layer owns the scheme declaration (convention-enforced boundary).
                JwtIdentityOpenApi.ApplySecuritySchemes(document);
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        // EnableRetryOnFailure is safe because the aggregates and the outbox rows go into one
        // SaveChanges with no explicit transaction. A handler that does open a transaction must
        // run it inside CreateExecutionStrategy().ExecuteAsync, as CreateOrderCommandHandler does;
        // otherwise the first transient fault throws. The DomainEventsInterceptor is stateless,
        // so one instance serves every context. The OwnerAuthorizationWriteGuard is scoped because
        // it reads the request's OwnerPolicyEvaluationTracker.
        services.TryAddScoped<OwnerPolicyEvaluationTracker>();
        services.AddScoped<OwnerAuthorizationWriteGuard>();
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
            options.UseNpgsql(connectionString, postgres =>
                postgres.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorCodesToAdd: null))
                   .EnableSensitiveDataLogging(false)
                   .AddInterceptors(new DomainEventsInterceptor(), provider.GetRequiredService<OwnerAuthorizationWriteGuard>()));

        // The Dapper connection is transient, not scoped: Npgsql cannot run two queries on one
        // connection at the same time, so each query handler gets its own. The physical
        // connections are pooled, so this costs little. The query handlers wrap their calls in
        // PostgresRetryPolicy.ExecuteAsync.
        services.AddTransient<System.Data.IDbConnection>(provider =>
            new Npgsql.NpgsqlConnection(connectionString));

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
                          // X-Correlation-ID is a documented client-settable request header (echoed on
                          // responses); omitting it here blocks browser callers from supplying their own.
                          .WithHeaders("Authorization", "Content-Type", "X-Correlation-ID");

                // Browsers hide non-safelisted response headers from cross-origin scripts unless the
                // policy exposes them. WWW-Authenticate carries the machine-actionable scope/step-up
                // challenge, X-Correlation-ID is the support handle, Retry-After the 429 back-off.
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

        // Self-contained JWTs only: the handler's ConfigurationManager caches discovery + JWKS in
        // memory (rate-limited refresh on unknown kid), so steady-state validation is a CPU-only
        // asymmetric verify. Never add a token-introspection call to this path.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<Microsoft.Extensions.Options.IOptions<JwtIdentityOptions>>((bearer, identityOptions) =>
            {
                var identity = identityOptions.Value;
                if (!string.IsNullOrWhiteSpace(identity.Authority))
                    bearer.Authority = identity.Authority;
                bearer.RequireHttpsMetadata = identity.RequireHttpsMetadata;
                // Keep raw OIDC claim types (sub/tid/scope/amr) — inbound claim remapping would
                // rename them out from under JwtIdentityMiddleware.
                bearer.MapInboundClaims = false;
                // The raw token is never needed after validation. Not saving it keeps
                // HttpContext.GetTokenAsync("access_token") from handing it back to app code.
                bearer.SaveToken = false;
                // Only asymmetric signatures. The JWKS keys are RSA or EC, so an HMAC or "none"
                // token would fail anyway; pinning the list makes that explicit.
                bearer.TokenValidationParameters.ValidAlgorithms = JwtIdentityOptions.AllowedSigningAlgorithms;
                bearer.TokenValidationParameters.ValidateAudience = true;
                bearer.TokenValidationParameters.ValidAudience = identity.Audience;
                bearer.TokenValidationParameters.ValidateIssuer = true;
                bearer.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(identity.ClockSkewSeconds);
                bearer.TokenValidationParameters.NameClaimType = "sub";
            });

        // Every endpoint requires an authenticated caller unless it says AllowAnonymous. The
        // convention tests check the /api/v1 routes; this catches a route mapped anywhere else.
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

    // Authenticated traffic is partitioned by the verified token identity so one tenant
    // cannot starve another; only unauthenticated traffic falls back to client IP.
    internal static string ResolveRateLimitPartitionKey(HttpContext httpContext)
    {
        var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();
        return currentUser is { IsAuthenticated: true }
            ? $"identity:{currentUser.TenantId}:{currentUser.Subject}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    // Fail at startup, not on the first token: a relative or malformed authority, or a plain-http
    // authority alongside RequireHttpsMetadata, otherwise surfaces only when the bearer handler
    // builds its metadata address. Presence is validated separately (environment-gated).
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

    // The "durable" tag marks checks against deployable backing resources; /healthiness runs
    // exactly that set. Service Bus and the payload archive register conditionally, mirroring
    // their service registrations, so standalone dev/tests without them stay healthy.
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready", "durable"]);

        // Only a real distributed cache (Redis) is a durable backing resource. The in-memory
        // fallback (Program.cs, when no redis connection string) is per-process, so tagging it
        // "durable" would make /healthiness green for a non-durable cache and mask a missing Redis.
        if (!string.IsNullOrEmpty(configuration.GetConnectionString("redis")))
            healthChecks.AddCheck<DistributedCacheHealthCheck>("distributed-cache", tags: ["durable"]);

        if (!string.IsNullOrEmpty(configuration.GetConnectionString("servicebus")))
            healthChecks.AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["durable"]);

        if (StarterApp.ServiceDefaults.Payloads.PayloadArchiveConfiguration.IsConfigured(configuration))
        {
            // AddCheck<T> alone activates a fresh instance on every health run. Registering the
            // check in DI makes HealthCheckService reuse one instance and the shared client.
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
            // The no-op fallback exists for tests and standalone dev only. In production-like
            // environments a missing/typo'd connection string would otherwise boot green, pass
            // /health/ready (database-only), and silently accumulate outbox rows forever — so
            // fail startup loudly, mirroring the Identity options environment gate.
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
